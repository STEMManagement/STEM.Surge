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
using STEM.Sys.Messaging;

namespace STEM.Surge.Messages
{

    /// <summary>
    /// Message sent from a SurgeActor to a DeploymentManager and from a DeploymentManager to a BranchManager requesting the branch delete all errored InstructionSets
    /// </summary>
    public class ClearErrors : STEM.Sys.Messaging.Message
    {
        public string BranchIP { get; set; }
        public List<string> SpecificErrors { get; set; }

        public ClearErrors()
        {
            SpecificErrors = new List<string>();
        }

        public ClearErrors(string branchIP)
        {
            BranchIP = branchIP;
            SpecificErrors = new List<string>();
        }

        public ClearErrors(string branchIP, List<string> specificErrors)
        {
            BranchIP = branchIP;
            SpecificErrors = specificErrors;
        }
    }
}
