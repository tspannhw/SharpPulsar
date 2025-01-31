﻿
using SharpPulsar.Interfaces;
using System;
using System.Collections.Generic;
using static SharpPulsar.Exceptions.PulsarClientException;

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
	/// Factory class that allows to create <seealso cref="Authentication"/> instances
	/// for all the supported authentication methods.
	/// </summary>
	public sealed class AuthenticationFactory
	{

		/// <summary>
		/// Create an authentication provider for token based authentication.
		/// </summary>
		/// <param name="token">
		///            the client auth token </param>
		/// <returns> the Authentication object initialized with the token credentials </returns>
		public static IAuthentication Token(string token)
		{
			return DefaultImplementation.NewAuthenticationToken(token);
		}

        public static IAuthentication Sts(string client, string secret, string authority)
        {
            return DefaultImplementation.NewAuthenticationSts(client,  secret, authority);
        }
		/// <summary>
		/// Create an authentication provider for token based authentication.
		/// </summary>
		/// <param name="tokenSupplier">
		///            a supplier of the client auth token </param>
		/// <returns> the Authentication object initialized with the token credentials </returns>
		public static IAuthentication Token(Func<string> tokenSupplier)
		{
			return DefaultImplementation.NewAuthenticationToken(tokenSupplier);
		}
        
        public static IAuthentication Tls(string pfxFilePath)
        {
            return DefaultImplementation.NewAuthenticationTls(pfxFilePath);
        }
        // CHECKSTYLE.ON: MethodName

        /// <summary>
        /// Create an instance of the <seealso cref="Authentication"/> object by using
        /// the plugin class name.
        /// </summary>
        /// <param name="authPluginClassName">
        ///            name of the Authentication-Plugin you want to use </param>
        /// <param name="authParamsString">
        ///            string which represents parameters for the Authentication-Plugin, e.g., "key1:val1,key2:val2" </param>
        /// <returns> instance of the Authentication object </returns>
        /// <exception cref="UnsupportedAuthenticationException"> </exception>
        public static IAuthentication Create(string authPluginClassName, string authParamsString)
		{
			try
			{
				return DefaultImplementation.CreateAuthentication(authPluginClassName, authParamsString);
			}
			catch (System.Exception t)
			{
				throw new UnsupportedAuthenticationException(t.Message);
			}
		}

		/// <summary>
		/// Create an instance of the Authentication-Plugin.
		/// </summary>
		/// <param name="authPluginClassName"> name of the Authentication-Plugin you want to use </param>
		/// <param name="authParams">          map which represents parameters for the Authentication-Plugin </param>
		/// <returns> instance of the Authentication-Plugin </returns>
		/// <exception cref="UnsupportedAuthenticationException"> </exception>
		public static IAuthentication Create(string authPluginClassName, IDictionary<string, string> authParams)
		{
			try
			{
				return DefaultImplementation.CreateAuthentication(authPluginClassName, authParams);
			}
			catch (System.Exception t)
			{
				throw new UnsupportedAuthenticationException(t.ToString());
			}
		}
	}

}