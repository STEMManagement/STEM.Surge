﻿/*
 * Copyright 2019 STEM Management
 *
 * Licensed under the Apache License, Version 2.0 (the "License");
 * you may not use this file except in compliance with the License.
 * You may obtain a copy of the License at
 *
 *   http://www.apache.org/licenses/LICENSE-2.0
 *
 * Unless required by applicable law or agreed to in writing, software
 * distributed under the License is distributed on an "AS IS" BASIS,
 * WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
 * See the License for the specific language governing permissions and
 * limitations under the License.
 * 
 */

using System;
using System.Collections.Generic;
using System.Linq;
using STEM.Sys.Threading;
using STEM.Sys.Messaging;
using System.Security.Cryptography.X509Certificates;

namespace STEM.Sys.IO.TCP
{
    public class MessageConnection : StringConnection
    {
        ThreadedQueue _ResponseReceivedQueue = null;

        Queue<Message> _Waiting = new Queue<Message>();
        Dictionary<Guid, Message> _LocalWaiting = new Dictionary<Guid, Message>();

        public MessageConnection(string address, int port, bool sslConnection, bool autoReconnect = false)
            : base(address, port, sslConnection, autoReconnect)
        {
            _ResponseReceivedQueue = new ThreadedQueue(address + ":" + port, TimeSpan.FromSeconds(20));
            _ResponseReceivedQueue.receiveNext += ProcessResponse;
        }

        public MessageConnection(string address, int port, bool sslConnection, X509Certificate2 certificate, bool autoReconnect = false)
            : base(address, port, sslConnection, certificate, autoReconnect)
        {
            _ResponseReceivedQueue = new ThreadedQueue(address + ":" + port, TimeSpan.FromSeconds(20));
            _ResponseReceivedQueue.receiveNext += ProcessResponse;
        }

        public MessageConnection(System.Net.Sockets.TcpClient client, X509Certificate2 certificate)
            : base(client, certificate)
        {
            _ResponseReceivedQueue = new ThreadedQueue(RemoteAddress + ":" + RemotePort, TimeSpan.FromSeconds(20));
            _ResponseReceivedQueue.receiveNext += ProcessResponse;
        }

        public sealed override void Close()
        {
            base.Close();

            if (_ResponseReceivedQueue != null)
            {
                _ResponseReceivedQueue.Dispose();
                _ResponseReceivedQueue = null;
            }

            List<Message> awaiting = null;
            lock (_AwaitResponse)
            {
                awaiting = _AwaitResponse.Values.ToList();
                _AwaitResponse.Clear();
            }

            foreach (Message ar in awaiting)
            {
                StopWaiting(ar);

                ConnectionLost m = new ConnectionLost(ar.MessageID);

                m.MessageConnection = this;
                m.TimeSent = m.TimeReceived = DateTime.UtcNow;

                try
                {
                    if (_ReceiveResponse != null)
                        _ReceiveResponse(this, ar, m);
                }
                catch { }
            }
        }

        class ResponseHandlerThread : STEM.Sys.Threading.IThreadable
        {
            static STEM.Sys.Threading.ThreadPool _ResponsePool = new STEM.Sys.Threading.ThreadPool(TimeSpan.FromMilliseconds(100), Environment.ProcessorCount);

            MessageConnection _MessageConnection = null;
            Message _Original = null;
            Message _Response = null;

            public ResponseHandlerThread(MessageConnection connection, Message original, Message response)
            {
                _MessageConnection = connection;
                _Original = original;
                _Response = response;

                _ResponsePool.RunOnce(this);
            }

            protected override void Execute(Sys.Threading.ThreadPool owner)
            {
                try
                {
                    if (_MessageConnection._ReceiveResponse != null)
                        _MessageConnection._ReceiveResponse(_MessageConnection, _Original, _Response);
                    else
                        _Original.ReceiveResponse(_Response);
                }
                catch (Exception ex)
                {
                    STEM.Sys.EventLog.WriteEntry(System.Reflection.Assembly.GetEntryAssembly().GetName().Name + ".MessageConnection.ResponseHandlerThread", ex.ToString(), STEM.Sys.EventLog.EventLogEntryType.Error);
                }
            }
        }

        void EnqueueResponse(Guid awaitID, Message orig, Message response)
        {
            lock (_LocalWaiting)
            {
                if (_LocalWaiting.ContainsKey(orig.MessageID))
                {
                    _LocalWaiting[orig.MessageID] = response;
                    orig.onResponse -= Waiting_onResponse;

                    while (true)
                        try
                        {
                            if (!_AwaitResponse.ContainsKey(awaitID))
                                return;

                            break;
                        }
                        catch { }
                }
            }

            if (_ResponseReceivedQueue != null)
                _ResponseReceivedQueue.EnqueueObject(new object[] { orig, response });
        }

        void ProcessResponse(ThreadedQueue sender, object next)
        {
            object[] args = next as object[];

            new ResponseHandlerThread(this, (Message)args[0], (Message)args[1]);
        }

        void ProcessMessageReceived(ThreadedQueue sender, object next)
        {
            object[] args = next as object[];
            try
            {
                ProcessMessageReceived((string)args[0], (DateTime)args[1]);
            }
            catch (Exception ex)
            {
                STEM.Sys.EventLog.WriteEntry(System.Reflection.Assembly.GetEntryAssembly().GetName().Name + ".MessageConnection.ProcessMessageReceived", ex.ToString(), STEM.Sys.EventLog.EventLogEntryType.Error);
            }
        }

        void ProcessMessageReceived(string m, DateTime received)
        {
            try
            {
                if (m.Contains("STEM.Sys.Messaging.MessageReceived"))
                {
                    STEM.Sys.EventLog.WriteEntry(System.Reflection.Assembly.GetEntryAssembly().GetName().Name + ".MessageConnection.ProcessMessageReceived", "MessageReceived received from " + this.RemoteAddress + ":" + this.LocalPort, STEM.Sys.EventLog.EventLogEntryType.Information);
                }

                Message message = Message.Deserialize(m) as Message;

                if (message != null)
                {
                    message.TimeReceived = received;
                    message.MessageConnection = this;

                    ProcessMessageReceived(message);
                }
            }
            catch (Exception ex)
            {
                STEM.Sys.EventLog.WriteEntry(System.Reflection.Assembly.GetEntryAssembly().GetName().Name + ".MessageConnection.ProcessMessageReceived", RemoteAddress + ":" + RemotePort + " \r\n" + ex.ToString(), STEM.Sys.EventLog.EventLogEntryType.Error);
            }
        }

        protected virtual void ProcessMessageReceived(Message message)
        {
            Message m = message;

            try
            {
                if (message is Response)
                {
                    Response response = (Response)message;
                    response.Message.TimeSent = message.TimeSent;
                    response.Message.TimeReceived = message.TimeReceived;
                    response.Message.MessageConnection = this;

                    Message orig = null;
                    while (true)
                        try
                        {
                            if (_AwaitResponse.ContainsKey(response.RespondingTo))
                                orig = _AwaitResponse[response.RespondingTo];

                            break;
                        }
                        catch { System.Threading.Thread.Sleep(10); }

                    if (orig != null)
                    {
                        EnqueueResponse(response.RespondingTo, orig, response.Message);
                        return;
                    }

                    response.Message.MessageID = message.MessageID;
                    m = response.Message;
                }

                try
                {
                    if (_Received != null)
                    {
                        if (_Waiting.Count > 0)
                        {
                            List<Message> waiting = new List<Message>();
                            lock (_Waiting)
                            {
                                waiting = _Waiting.ToList();
                                _Waiting.Clear();
                            }

                            foreach (Message w in waiting)
                                try
                                {
                                    _Received(this, w);
                                }
                                catch (Exception ex)
                                {
                                    STEM.Sys.EventLog.WriteEntry(System.Reflection.Assembly.GetEntryAssembly().GetName().Name + ".MessageConnection.ProcessMessageReceived", ex.ToString(), STEM.Sys.EventLog.EventLogEntryType.Error);
                                }
                        }

                        try
                        {
                            _Received(this, m);
                        }
                        catch (Exception ex)
                        {
                            STEM.Sys.EventLog.WriteEntry(System.Reflection.Assembly.GetEntryAssembly().GetName().Name + ".MessageConnection.ProcessMessageReceived", ex.ToString(), STEM.Sys.EventLog.EventLogEntryType.Error);
                        }
                    }
                    else
                    {
                        lock (_Waiting)
                            _Waiting.Enqueue(m);
                    }
                }
                catch (Exception ex)
                {
                    STEM.Sys.EventLog.WriteEntry(System.Reflection.Assembly.GetEntryAssembly().GetName().Name + ".MessageConnection.ProcessMessageReceived", ex.ToString(), STEM.Sys.EventLog.EventLogEntryType.Error);
                }
            }
            catch (Exception ex)
            {
                STEM.Sys.EventLog.WriteEntry(System.Reflection.Assembly.GetEntryAssembly().GetName().Name + ".MessageConnection.ProcessMessageReceived", RemoteAddress + ":" + RemotePort + " \r\n" + ex.ToString(), STEM.Sys.EventLog.EventLogEntryType.Error);
            }
        }

        public override void Receive(string message, DateTime received)
        {
            try
            {
                if (message.Contains("STEM.Sys.Messaging.ConnectionTest"))
                    return;

                ProcessMessageReceived(message, received);
            }
            catch (Exception ex)
            {
                STEM.Sys.EventLog.WriteEntry(System.Reflection.Assembly.GetEntryAssembly().GetName().Name + ".MessageConnection.Receive", RemoteAddress + ":" + RemotePort + " \r\n" + ex.ToString(), STEM.Sys.EventLog.EventLogEntryType.Error);
            }
        }

        public delegate void Received(MessageConnection connection, Message message);
        Received _Received;
        public event Received onReceived
        {
            add
            {
                if (value == null)
                    return;

                lock (_AwaitResponse)
                    if (_Received == null)
                    {
                        _Received = value;
                    }
                    else if (_Received != value)
                    {
                        throw new Exception("Event onReceived is currently in use by another delegate. Only one use of onReceived is permitted at a time.");
                    }
            }

            remove
            {
                if (value == null)
                    return;

                lock (_AwaitResponse)
                    if (_Received == value)
                        _Received = null;
            }
        }

        public delegate void ReceiveResponse(MessageConnection connection, Message original, Message response);
        ReceiveResponse _ReceiveResponse;
        public event ReceiveResponse onResponseReceived
        {
            add
            {
                lock (_AwaitResponse)
                    if (_ReceiveResponse == null)
                    {
                        _ReceiveResponse = value;
                    }
                    else if (_ReceiveResponse != value)
                    {
                        throw new Exception("Event onResponseReceived is currently in use by another delegate. Only one use of onResponseReceived is permitted at a time.");
                    }
            }

            remove
            {
                lock (_AwaitResponse)
                    if (_ReceiveResponse == value)
                        _ReceiveResponse = null;
            }
        }

        protected override void ConnectionReset()
        {
            try
            {
                base.ConnectionReset();
            }
            catch { }

            lock (_LocalWaiting)
                foreach (Guid id in _LocalWaiting.Keys.ToList())
                    _LocalWaiting[id] = new Undeliverable(id);

            List<Message> awaiting = new List<Message>();
            lock (_AwaitResponse)
            {
                awaiting = _AwaitResponse.Values.ToList();
                _AwaitResponse.Clear();
            }

            foreach (Message orig in awaiting)
            {
                orig.onResponse -= Waiting_onResponse;
                StopWaiting(orig);

                if (_ResponseReceivedQueue != null)
                    _ResponseReceivedQueue.EnqueueObject(new object[] { orig, new Undeliverable(orig.MessageID) });
            }
        }

        public Message Send(Message message, TimeSpan waitForResponse, bool acceptResponsesUntilDisposed = false)
        {
            if (message != null)
            {
                double wait = waitForResponse.TotalMilliseconds;
                
                lock (_LocalWaiting)
                {
                    if (!_LocalWaiting.ContainsKey(message.MessageID))
                    {
                        message.onResponse += Waiting_onResponse;
                        _LocalWaiting[message.MessageID] = null;
                    }
                    else
                    {
                        return new Undeliverable(message.MessageID);
                    }
                }

                Send(message, acceptResponsesUntilDisposed);

                Message ret = null;

                System.Diagnostics.Stopwatch sw = new System.Diagnostics.Stopwatch();
                sw.Start();

                try
                {
                    while (sw.ElapsedMilliseconds < wait)
                    {
                        lock (_LocalWaiting)
                        {
                            if (_LocalWaiting.ContainsKey(message.MessageID))
                                ret = _LocalWaiting[message.MessageID];

                            if (ret != null)
                            {
                                _LocalWaiting.Remove(message.MessageID);
                                return ret;
                            }
                        }

                        System.Threading.Thread.Sleep(25);
                    }
                }
                finally
                {
                    sw.Stop();
                }

                lock (_LocalWaiting)
                {
                    if (_LocalWaiting.ContainsKey(message.MessageID))
                    {
                        ret = _LocalWaiting[message.MessageID];
                        _LocalWaiting.Remove(message.MessageID);
                    }

                    if (ret == null)
                        message.onResponse -= Waiting_onResponse;
                }

                if (ret != null)
                    return ret;
                else
                    return new Timeout(message.MessageID);
            }
            
            return new Undeliverable(message.MessageID);
        }

        internal void Waiting_onResponse(Message delivered, Message response)
        {
        }

        Dictionary<Guid, Message> _AwaitResponse = new Dictionary<Guid, Message>();
        public bool Send(Message message, bool acceptResponsesUntilDisposed = false)
        {
            if (message != null)
            {
                message.AcceptResponsesUntilDisposed = acceptResponsesUntilDisposed;
                message.TimeSent = DateTime.UtcNow;

                string xml = message.Serialize();

                try
                {
                    message.RegisterSender(this);
                    
                    if (message is Response)
                    {
                        Response m = message as Response;

                        message.AcceptResponsesUntilDisposed = acceptResponsesUntilDisposed;

                        if (m.Message.AcceptingResponses || m.Message.AcceptResponsesUntilDisposed || message.AcceptingResponses)
                            lock (_AwaitResponse)
                            {
                                m.Message.onDisposing += Message_onDisposing;
                                _AwaitResponse[m.Message.MessageID] = m.Message;
                            }
                    }
                    else
                    {
                        if (message.AcceptingResponses || message.AcceptResponsesUntilDisposed)
                        {
                            lock (_AwaitResponse)
                            {
                                message.onDisposing += Message_onDisposing;
                                _AwaitResponse[message.MessageID] = message;
                            }
                        }
                    }

                    if (message.Compress)
                    {
                        if (!Send(xml))
                        {
                            throw new Exception("Message delivery failure: Type (" + message.GetType().AssemblyQualifiedName + ", IP(" + RemoteAddress + "), Port(" + RemotePort + ")");
                        }
                        else
                        {
                            return true;
                        }
                    }
                    else
                    {
                        byte[] b = System.Text.Encoding.Unicode.GetBytes(xml);
                        if (!Send(STEM.Sys.IO.ByteCompression.FauxCompress(b, b.Length)))
                        {
                            throw new Exception("Message delivery failure: Type (" + message.GetType().AssemblyQualifiedName + ", IP(" + RemoteAddress + "), Port(" + RemotePort + ")");
                        }
                        else
                        {
                            return true;
                        }
                    }
                }
                catch (Exception ex)
                {
                    if (message is Response)
                    {
                        Response m = message as Response;
                        if (m.Message.AcceptingResponses || m.Message.AcceptResponsesUntilDisposed || message.AcceptingResponses)
                        {
                            m.Message.AcceptResponsesUntilDisposed = false;
                            EnqueueResponse(m.Message.MessageID, m.Message, new Undeliverable(m.Message.MessageID));
                        }
                    }

                    if (message.AcceptingResponses || message.AcceptResponsesUntilDisposed)
                    {
                        message.AcceptResponsesUntilDisposed = false;
                        EnqueueResponse(message.MessageID, message, new Undeliverable(message.MessageID));
                    }

                    STEM.Sys.EventLog.WriteEntry(System.Reflection.Assembly.GetEntryAssembly().GetName().Name + ".MessageConnection.Send", ex.ToString(), STEM.Sys.EventLog.EventLogEntryType.Error);
                }
            }

            return false;
        }

        internal void StopWaiting(Message message)
        {
            if (message != null)
            {
                if (message is Response)
                {
                    Response m = message as Response;

                    lock (_AwaitResponse)
                    {
                        m.Message.onDisposing -= Message_onDisposing;

                        if (_AwaitResponse.ContainsKey(m.Message.MessageID))
                            _AwaitResponse.Remove(m.Message.MessageID);
                    }
                }

                lock (_AwaitResponse)
                {
                    message.onDisposing -= Message_onDisposing;

                    if (_AwaitResponse.ContainsKey(message.MessageID))
                        _AwaitResponse.Remove(message.MessageID);
                }
            }
        }

        void Message_onDisposing(object sender, EventArgs e)
        {
            StopWaiting(sender as Message);
        }
    }
}
