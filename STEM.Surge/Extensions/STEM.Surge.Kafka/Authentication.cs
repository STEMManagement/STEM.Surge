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
using System.Reflection;
using System.ComponentModel;
using System.Xml.Serialization;
using STEM.Sys.Security;
using Confluent.Kafka;

namespace STEM.Surge.Kafka
{
    public class Authentication : Sys.Security.IAuthentication
    {
        [Category("Kafka Server")]
        [DisplayName("Server Address"), DescriptionAttribute("What is the Server Address?")]
        public string ServerAddress { get; set; }

        [Category("Kafka Server")]
        [DisplayName("Server Port"), DescriptionAttribute("What is the Server Port?")]
        public string Port { get; set; }

        [Category("Kafka Server")]
        [DisplayName("Topic Name"), Description("The Topic being addressed.")]
        public string TopicName { get; set; }

        [Category("Kafka Server")]
        [DisplayName("Ssl Certificate Location"), Description("The location of the PEM file.")]
        public string SslCaLocation { get; set; }

        [Category("Kafka Server")]
        [DisplayName("Security Protocol"), Description("The Security Protocol to be used.")]
        public SecurityProtocol SecurityProtocol { get; set; }

        [Category("Kafka Server")]
        [DisplayName("Sasl Mechanism"), Description("The Sasl Mechanism to be used.")]
        public SaslMechanism SaslMechanism { get; set; }

        [Category("Kafka Server")]
        [DisplayName("Sasl Username"), Description("The Sasl Username to be used.")]
        public string SaslUsername { get; set; }
               
        [Category("Kafka Server")]
        [DisplayName("Sasl Password"), DescriptionAttribute("The Sasl Password to be used?")]
        [XmlIgnore]
        [PasswordPropertyText(true)]
        public string SaslPassword { get; set; }
        [Browsable(false)]
        public string SaslPasswordEncoded
        {
            get
            {
                return this.Entangle(SaslPassword);
            }

            set
            {
                SaslPassword = this.Detangle(value);
            }
        }


        static Dictionary<string, STEM.Sys.State.GrabBag<string>> _ServerAddresses = new Dictionary<string, Sys.State.GrabBag<string>>(StringComparer.InvariantCultureIgnoreCase);

        public static string NextAddress(string rangedAddress)
        {
            if (!_ServerAddresses.ContainsKey(rangedAddress))
                lock (_ServerAddresses)
                    if (!_ServerAddresses.ContainsKey(rangedAddress))
                        _ServerAddresses[rangedAddress] = new Sys.State.GrabBag<string>(STEM.Sys.IO.Path.ExpandRangedIP(rangedAddress), rangedAddress);

            return _ServerAddresses[rangedAddress].Next();
        }

        public static void SuspendAddress(string rangedAddress, string suspendAddress)
        {
            if (!_ServerAddresses.ContainsKey(rangedAddress))
                lock (_ServerAddresses)
                    if (!_ServerAddresses.ContainsKey(rangedAddress))
                        _ServerAddresses[rangedAddress] = new Sys.State.GrabBag<string>(STEM.Sys.IO.Path.ExpandRangedIP(rangedAddress), rangedAddress);

            _ServerAddresses[rangedAddress].Suspend(suspendAddress);
        }

        public static void ResumeAddress(string rangedAddress, string resumeAddress)
        {
            if (!_ServerAddresses.ContainsKey(rangedAddress))
                lock (_ServerAddresses)
                    if (!_ServerAddresses.ContainsKey(rangedAddress))
                        _ServerAddresses[rangedAddress] = new Sys.State.GrabBag<string>(STEM.Sys.IO.Path.ExpandRangedIP(rangedAddress), rangedAddress);

            _ServerAddresses[rangedAddress].Resume(resumeAddress);
        }

        public Authentication()
        {            
            string platform = "";
            string lib = "";
            
            if (STEM.Sys.Control.IsWindows)
            {
                lib = "librdkafka.dll";
                platform = "win-x86";
                if (STEM.Sys.Control.IsX64)
                    platform = "win-x64";
            }
            else
            {
                lib = "librdkafka.so";
                platform = "linux-x86";
                if (STEM.Sys.Control.IsX64)
                    platform = "linux-x64";
            }

            foreach (string f in STEM.Sys.IO.Directory.STEM_GetFiles(STEM.Sys.Serialization.VersionManager.VersionCache, lib, platform, System.IO.SearchOption.AllDirectories, false))
                lib = f;

            if (STEM.Sys.Control.IsWindows)
                Confluent.Kafka.Library.Load(lib);
            else
                System.IO.File.Copy(lib, System.IO.Path.Combine(Environment.CurrentDirectory, System.IO.Path.GetFileName(lib)), true);

            ServerAddress = "[QueueServerAddress]";
            Port = "[QueueServerPort]";

            TopicName = "[TopicName]";
            SslCaLocation = "";
            SecurityProtocol = SecurityProtocol.SaslSsl;
            SaslMechanism = SaslMechanism.ScramSha256;
            SaslUsername = "";
            SaslPassword = "";
        }

        public override void PopulateFrom(Sys.Security.IAuthentication source)
        {
            if (source.VersionDescriptor.TypeName == VersionDescriptor.TypeName)
            {
                PropertyInfo i = source.GetType().GetProperties().FirstOrDefault(p => p.Name == "ServerAddress");
                if (i != null)
                {
                    string k = i.GetValue(source) as string;
                    if (!String.IsNullOrEmpty(k) && String.IsNullOrEmpty(ServerAddress))
                        ServerAddress = k;
                }

                i = source.GetType().GetProperties().FirstOrDefault(p => p.Name == "Port");
                if (i != null)
                {
                    string k = i.GetValue(source) as string;
                    if (!String.IsNullOrEmpty(k) && String.IsNullOrEmpty(Port))
                        Port = k;
                }

                i = source.GetType().GetProperties().FirstOrDefault(p => p.Name == "TopicName");
                if (i != null)
                {
                    string k = i.GetValue(source) as string;
                    if (!String.IsNullOrEmpty(k) && String.IsNullOrEmpty(TopicName))
                        TopicName = k;
                }

                i = source.GetType().GetProperties().FirstOrDefault(p => p.Name == "SslCaLocation");
                if (i != null)
                {
                    string k = i.GetValue(source) as string;
                    if (!String.IsNullOrEmpty(k) && String.IsNullOrEmpty(SslCaLocation))
                        SslCaLocation = k;
                }

                i = source.GetType().GetProperties().FirstOrDefault(p => p.Name == "SaslUsername");
                if (i != null)
                {
                    string k = i.GetValue(source) as string;
                    if (!String.IsNullOrEmpty(k) && String.IsNullOrEmpty(SaslUsername))
                        SaslUsername = k;
                }

                i = source.GetType().GetProperties().FirstOrDefault(p => p.Name == "SaslPassword");
                if (i != null)
                {
                    string k = i.GetValue(source) as string;
                    if (!String.IsNullOrEmpty(k) && String.IsNullOrEmpty(SaslPassword))
                        SaslPassword = k;
                }

                i = source.GetType().GetProperties().FirstOrDefault(p => p.Name == "SecurityProtocol");
                if (i != null)
                {
                    SecurityProtocol = (SecurityProtocol)i.GetValue(source);
                }

                i = source.GetType().GetProperties().FirstOrDefault(p => p.Name == "SaslMechanism");
                if (i != null)
                {
                    SaslMechanism = (SaslMechanism)i.GetValue(source);
                }
            }
            else
            {
                throw new Exception("IAuthentication Type mismatch.");
            }
        }

        [XmlIgnore]
        [Browsable(false)]
        public ProducerConfig ProducerConfig
        {
            get
            {
                switch (SecurityProtocol)
                {
                    case SecurityProtocol.Ssl:
                        return new ProducerConfig
                        {
                            BootstrapServers = NextAddress(ServerAddress) + ":" + Port,
                            SslCaLocation = this.SslCaLocation,
                            SecurityProtocol = this.SecurityProtocol
                        };

                    case SecurityProtocol.SaslSsl:
                    case SecurityProtocol.SaslPlaintext:
                        return new ProducerConfig
                        {
                            BootstrapServers = NextAddress(ServerAddress) + ":" + Port,
                            SecurityProtocol = this.SecurityProtocol,
                            SaslMechanism = this.SaslMechanism,
                            SaslUsername = this.SaslUsername,
                            SaslPassword = this.SaslPassword
                        };

                    default:
                        return new ProducerConfig
                        {
                            BootstrapServers = NextAddress(ServerAddress) + ":" + Port,
                            SecurityProtocol = this.SecurityProtocol
                        };
                }
            }
        }

        public ConsumerConfig ConsumerConfig(string groupID, AutoOffsetReset autoOffsetReset = AutoOffsetReset.Earliest)
        {
            switch (SecurityProtocol)
            {
                case SecurityProtocol.Ssl:
                    return new ConsumerConfig
                    {
                        GroupId = groupID,
                        BootstrapServers = NextAddress(ServerAddress) + ":" + Port,
                        SslCaLocation = this.SslCaLocation,
                        SecurityProtocol = this.SecurityProtocol,
                        AutoOffsetReset = autoOffsetReset
                    };

                case SecurityProtocol.SaslSsl:
                case SecurityProtocol.SaslPlaintext:
                    return new ConsumerConfig
                    {
                        GroupId = groupID,
                        BootstrapServers = NextAddress(ServerAddress) + ":" + Port,
                        SecurityProtocol = this.SecurityProtocol,
                        SaslMechanism = this.SaslMechanism,
                        SaslUsername = this.SaslUsername,
                        SaslPassword = this.SaslPassword,
                        AutoOffsetReset = autoOffsetReset
                    };

                default:
                    return new ConsumerConfig
                    {
                        GroupId = groupID,
                        BootstrapServers = NextAddress(ServerAddress) + ":" + Port,
                        SecurityProtocol = this.SecurityProtocol,
                        AutoOffsetReset = autoOffsetReset
                    };
            }
        }
    }
}
