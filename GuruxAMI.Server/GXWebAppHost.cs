//
// --------------------------------------------------------------------------
//  Gurux Ltd
// 
//
//
// Filename:        $HeadURL$
//
// Version:         $Revision$,
//                  $Date$
//                  $Author$
//
// Copyright (c) Gurux Ltd
//
//---------------------------------------------------------------------------
//
//  DESCRIPTION
//
// This file is a part of Gurux Device Framework.
//
// Gurux Device Framework is Open Source software; you can redistribute it
// and/or modify it under the terms of the GNU General Public License 
// as published by the Free Software Foundation; version 2 of the License.
// Gurux Device Framework is distributed in the hope that it will be useful,
// but WITHOUT ANY WARRANTY; without even the implied warranty of 
// MERCHANTABILITY or FITNESS FOR A PARTICULAR PURPOSE. 
// See the GNU General Public License for more details.
//
// This code is licensed under the GNU General Public License v2. 
// Full text may be retrieved at http://www.gnu.org/licenses/gpl-2.0.txt
//---------------------------------------------------------------------------

using System;
using System.Reflection;
using Funq;
using ServiceStack.OrmLite;
using ServiceStack.ServiceHost;
using ServiceStack.ServiceInterface;
using ServiceStack.ServiceInterface.Auth;
using ServiceStack.WebHost.Endpoints;
using ServiceStack.ServiceInterface.Cors;

namespace GuruxAMI.Server
{
    internal class GXWebAppHost : AppHostBase
    {
        IDbConnectionFactory ConnectionFactory = null;
        internal string Prefix;

        //Tell Service Stack the name of your application and where to find your web services
        public GXWebAppHost(IDbConnectionFactory connectionFactory, string prefix)
            : base("GuruxAMI Web Service", Assembly.GetExecutingAssembly())
        {
            if (connectionFactory == null)
            {
                throw new ArgumentNullException("connectionFactory");
            }
            Prefix = prefix;
            ConnectionFactory = connectionFactory;
        }

        /// <summary>
        /// Add general error listener.
        /// </summary>
        /// <typeparam name="TRequest"></typeparam>
        /// <param name="actionContext"></param>
        /// <returns></returns>
        public override IServiceRunner<TRequest> CreateServiceRunner<TRequest>(ActionContext actionContext)
        {
            return new GXServiceRunner<TRequest>(this, actionContext);
        }

        public override void Configure(Container container)
        {
            /*
            //Permit modern browsers (e.g. Firefox) to allow sending of any REST HTTP Method
            base.SetConfig(new EndpointHostConfig
            {
                GlobalResponseHeaders = {
                    { "Access-Control-Allow-Origin", "*" },
                    { "Access-Control-Allow-Methods", "GET, POST, PUT, OPTIONS" },
                    { "Access-Control-Allow-Headers", "Content-Type, Authorization" },
                },
            });
            */
            //Set table prefix.
            if (!string.IsNullOrEmpty(Prefix))
            {
                ServiceStack.OrmLite.OrmLiteConfig.DialectProvider.NamingStrategy = new GXPrefixNamingStrategy { TablePrefix = Prefix };
            }
            container.Register<IDbConnectionFactory>(ConnectionFactory);
            container.Register<AppHost>(new AppHost());
            //Add Cors for jQuery.
            Plugins.Add(new CorsFeature("*", "GET, POST, DELETE, OPTIONS", "Content-Type, Authorization", false));
            //Basic Authentication is asked when connection is made.            
            Plugins.Add(new AuthFeature(() => new AuthUserSession(), new IAuthProvider[] {
                              new GXBasicAuthProvider()}, null));
        }
    }
}
