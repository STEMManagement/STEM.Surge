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
using System.IO;
using System.Xml.Linq;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;

namespace STEM.Surge.XML
{
    [TypeConverter(typeof(ExpandableObjectConverter))]
    [DisplayName("Xml Consuming Controller")]
    [Description("Customize an InstructionSet Template using the node names from an XmlNode as implicit [PlaceHolders]. " +
        "All documents found in the Source folder(s), limited by Directory Filter and File Filter, will be consumed such that as each node is successfully processed the source document will be " +
        "modified to remove that node, and when the document is empty the source file will be deleted. " +
        "For each Key Element tuple, an InstructionSet Template will be customized with all possible placeholder replacements for all [NodeName] values from that node.")]
    public class XmlConsumingController : STEM.Surge.DeploymentController
    {
        [Category("XML Controller")]
        [DisplayName("Key Element(s)"), DescriptionAttribute("The element(s) (comma separated to use as the node 'key' for a document.")]
        public string KeyElements { get; set; }

        public XmlConsumingController()
        {
            KeyElements = "MyKeyElementName1,MyKeyElementName2";
        }

        static List<XmlDoc> _Documents = new List<XmlDoc>();
        Dictionary<string, XmlDoc> _PreProcessResult = new Dictionary<string, XmlDoc>();
        Dictionary<Guid, XmlDoc> _Assignments = new Dictionary<Guid, XmlDoc>();

        class XmlDoc
        {
            public string Filename { get; set; }
            public DateTime OriginalLastWriteTime { get; set; }
            public XDocument Document { get; set; }
            public Dictionary<string, XElement> Nodes { get; set; }
        }

        public override List<string> ListPreprocess(IReadOnlyList<string> list)
        {
            _PreProcessResult = new Dictionary<string, XmlDoc>();
            List<string> errors = new List<string>();

            try
            {
                List<string> keys = KeyElements.Split(',').ToList();

                string firstKey = keys.FirstOrDefault();

                if (String.IsNullOrEmpty(firstKey))
                    throw new Exception("KeyElements has no value.");

                foreach (string d in list)
                {
                    XmlDoc doc = null;
                    try
                    {
                        lock (_Documents)
                        {
                            doc = _Documents.FirstOrDefault(i => i.Filename.Equals(d, StringComparison.InvariantCultureIgnoreCase));

                            if (doc != null)
                            {
                                System.Threading.Monitor.Enter(doc);

                                if (!File.Exists(doc.Filename) || doc.OriginalLastWriteTime == File.GetLastWriteTimeUtc(doc.Filename))
                                {
                                    foreach (string s in doc.Nodes.Keys)
                                        _PreProcessResult[s] = doc;

                                    continue;
                                }
                                else
                                {
                                    doc.OriginalLastWriteTime = File.GetLastWriteTimeUtc(d);
                                    doc.Document = XDocument.Parse(File.ReadAllText(d));
                                    doc.Nodes = new Dictionary<string, XElement>();
                                }
                            }
                            else
                            {
                                doc = new XmlDoc();
                                doc.Filename = d;
                                doc.OriginalLastWriteTime = File.GetLastWriteTimeUtc(d);
                                doc.Document = XDocument.Parse(File.ReadAllText(d));
                                doc.Nodes = new Dictionary<string, XElement>();

                                System.Threading.Monitor.Enter(doc);

                                _Documents.Add(doc);
                            }
                        }

                        foreach (XElement e in doc.Document.Descendants().Where(i => i.Name.LocalName.Equals(firstKey)).Select(i => i.Parent))
                        {
                            string fullKey = "";
                            foreach (string key in keys)
                            {
                                string s = e.Descendants().Where(i => i.Name.LocalName.Equals(key)).Select(i => i.Value).FirstOrDefault();
                                if (s == null)
                                {
                                    fullKey = null;
                                    break;
                                }

                                if (fullKey != "")
                                    fullKey += " ";

                                fullKey += s;
                            }

                            if (fullKey == null)
                                continue;

                            doc.Nodes[fullKey] = e;
                            _PreProcessResult[fullKey] = doc;
                        }
                    }
                    catch (Exception ex)
                    {
                        errors.Add("XmlConsumingController encountered an error processing " + d + ": " + ex.Message);
                        STEM.Sys.EventLog.WriteEntry("XmlConsumingController.ListPreprocess", d + ": " + ex.ToString(), STEM.Sys.EventLog.EventLogEntryType.Error);
                    }
                    finally
                    {
                        if (doc != null)
                            System.Threading.Monitor.Exit(doc);
                    }
                }

                if (errors.Count == 0)
                    PollError = "";
                else
                    throw new Exception(String.Join("\r\n", errors));
            }
            catch (Exception ex)
            {
                PollError = "(" + STEM.Sys.IO.Net.MachineIP() + ") encountered errors: \r\n" + ex.Message;
                STEM.Sys.EventLog.WriteEntry("XmlConsumingController.ListPreprocess", ex.ToString(), STEM.Sys.EventLog.EventLogEntryType.Error);
            }

            List<string> returnList = _PreProcessResult.Keys.ToList();
            if (HonorPriorityFilters)
                returnList = ApplyPriorityFilterOrdering(returnList);

            return returnList; 
        }
        
        public override DeploymentDetails GenerateDeploymentDetails(IReadOnlyList<string> listPreprocessResult, string initiationSource, string recommendedBranchIP, IReadOnlyList<string> limitedToBranches)
        {
            try
            {
                if (!_PreProcessResult.ContainsKey(initiationSource))
                    return null;

                XmlDoc doc = _PreProcessResult[initiationSource];

                System.Collections.Generic.Dictionary<string, string> kvp = TemplateKVP.ToDictionary(i => i.Key, i => i.Value);

                lock (doc)
                {
                    if (!doc.Nodes.ContainsKey(initiationSource))
                        return null;

                    XElement node = doc.Nodes[initiationSource];

                    foreach (XElement e in node.Descendants())
                        kvp[e.Name.LocalName] = e.Value;
                }

                DeploymentDetails ret = base.GenerateDeploymentDetails(listPreprocessResult, initiationSource, recommendedBranchIP, limitedToBranches);

                CustomizeInstructionSet(ret.ISet, kvp, ret.BranchIP, initiationSource, false);

                lock (_Assignments)
                    _Assignments[ret.ISet.ID] = doc;

                return ret;
            }
            catch (Exception ex)
            {
                STEM.Sys.EventLog.WriteEntry("XmlConsumingController.GenerateDeploymentDetails", new Exception(initiationSource, ex).ToString(), STEM.Sys.EventLog.EventLogEntryType.Error);
            }

            return null;
        }

        public override void ExecutionComplete(DeploymentDetails details, List<Exception> exceptions)
        {
            base.ExecutionComplete(details, exceptions);

            XmlDoc doc = null;
            lock (_Assignments)
            {
                if (!_Assignments.ContainsKey(details.InstructionSetID))
                    return;

                doc = _Assignments[details.InstructionSetID];
                _Assignments.Remove(details.InstructionSetID);
            }

            if (exceptions == null || exceptions.Count == 0)
                if (doc != null)
                {
                    lock (_Documents)
                        lock (doc)
                        {
                            if (!doc.Nodes.ContainsKey(details.InitiationSource))
                                return;

                            XElement e = doc.Nodes[details.InitiationSource];

                            doc.Nodes.Remove(details.InitiationSource);

                            e.Remove();

                            if (doc.OriginalLastWriteTime == File.GetLastWriteTimeUtc(doc.Filename))
                            {
                                doc.Document.Save(doc.Filename);

                                if (doc.Nodes.Count == 0)
                                {
                                    try
                                    {
                                        File.Delete(doc.Filename);
                                        _Documents.Remove(doc);
                                    }
                                    catch { }
                                }
                                else
                                {
                                    File.SetLastWriteTimeUtc(doc.Filename, doc.OriginalLastWriteTime);
                                }
                            }
                        }
                }
        }
    }
}
