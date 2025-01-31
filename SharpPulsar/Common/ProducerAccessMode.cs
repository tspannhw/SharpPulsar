﻿/// <summary>
/// Licensed to the Apache Software Foundation (ASF) under one
/// or more contributor license agreements.  See the NOTICE file
/// distributed with this work for additional information
/// regarding copyright ownership.  The ASF licenses this file
/// to you under the Apache License, Version 2.0 (the
/// "License"); you may not use this file except in compliance
/// with the License.  You may obtain a copy of the License at
/// 
///   http://www.apache.org/licenses/LICENSE-2.0
/// 
/// Unless required by applicable law or agreed to in writing,
/// software distributed under the License is distributed on an
/// "AS IS" BASIS, WITHOUT WARRANTIES OR CONDITIONS OF ANY
/// KIND, either express or implied.  See the License for the
/// specific language governing permissions and limitations
/// under the License.
/// </summary>

namespace SharpPulsar.Common
{
    /// <summary>
	/// The type of access to the topic that the producer requires.
	/// </summary>
	public enum ProducerAccessMode
    {
        /// <summary>
        /// By default multiple producers can publish on a topic.
        /// </summary>
        Shared,

        /// <summary>
        /// Require exclusive access for producer. Fail immediately if there's already a producer connected.
        /// </summary>
        Exclusive,

        /// <summary>
        /// Producer creation is pending until it can acquire exclusive access.
        /// </summary>
        WaitForExclusive,
    }
}
