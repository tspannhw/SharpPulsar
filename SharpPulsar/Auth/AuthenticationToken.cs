﻿
using SharpPulsar.Interfaces;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

/// <summary>
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

namespace SharpPulsar.Auth
{

    /// <summary>
    /// Token based authentication provider.
    /// </summary>
    public class AuthenticationToken : IAuthentication, IEncodedAuthenticationParameterSupport
	{

		private Func<string> _tokenSupplier;

		public AuthenticationToken()
		{
		}

		public AuthenticationToken(string token) : this(() => token)
		{
		}

		public AuthenticationToken(Func<string> tokenSupplier)
		{
			this._tokenSupplier = tokenSupplier;
		}

		public void Close()
		{
			// noop
		}

		public string AuthMethodName => "token";

		public IAuthenticationDataProvider AuthData => new AuthenticationDataToken(_tokenSupplier);

		public void Configure(string encodedAuthParamString)
		{
			// Interpret the whole param string as the token. If the string contains the notation `token:xxxxx` then strip
			// the prefix
			if (encodedAuthParamString.StartsWith("token:", StringComparison.Ordinal))
			{
				_tokenSupplier = () => encodedAuthParamString.Substring("token:".Length);
			}
			else if (encodedAuthParamString.EndsWith(".key", StringComparison.Ordinal))
			{
				// Read token from a file
				var filePath = encodedAuthParamString;
				_tokenSupplier = () =>
				{
					try
					{
						var by = File.ReadAllBytes(filePath);

						return Encoding.UTF8.GetString(by).Trim();
					}
					catch (IOException e)
					{
						throw new IOException("Failed to read token from file", e);
					}
				};
			}
			else
			{
				_tokenSupplier = () => encodedAuthParamString;
			}
		}

		public void Configure(IDictionary<string, string> authParams)
		{
			// noop
		}

		public void Start()
		{
			// noop
		}

		public void Dispose()
		{
		}
	}

}
