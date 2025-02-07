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
using System.ComponentModel;
using System.Xml.Serialization;
using STEM.Sys.IO.TCP;

namespace STEM.Sys.Messaging
{
    public class ConnectionLost : Message
    {
        /// <summary>
        /// A uniqueue ID used to track back to the orignal message
        /// </summary>
        [DisplayName("Orignal Message ID"), ReadOnlyAttribute(true)]
        public Guid ResponseToMessageID { get; set; }

        public ConnectionLost()
        {
        }

        public ConnectionLost(Guid responseToMessageID)
        {
            ResponseToMessageID = responseToMessageID;
        }
    }
}
