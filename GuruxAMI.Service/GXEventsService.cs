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

using Funq;
using ServiceStack.ServiceInterface;
using ServiceStack.ServiceInterface.Auth;
using ServiceStack.WebHost.Endpoints;
using System.Reflection;
using ServiceStack.OrmLite;
using System.Data;
using ServiceStack.ServiceHost;
using GuruxAMI.Common;
using System.Collections.Generic;
using System.Threading;
using System;
using GuruxAMI.Common.Messages;
using GuruxAMI.Server;

namespace GuruxAMI.Service
{
    /// <summary>
    /// Service handles event functionality.
    /// </summary>
	[Authenticate]
    internal class GXEventsService : ServiceStack.ServiceInterface.Service
	{
        public GXEventsUnregisterResponse Put(GXEventsUnregisterRequest request)
        {
            Guid guid = Guid.Empty;
            IAuthSession s = this.GetSession(false);            
            long id;
            if (!long.TryParse(s.Id, out id))
            {
                if (!GXBasicAuthProvider.IsGuid(s.UserAuthName, out guid))
                {
                    throw new ArgumentException("Access denied.");
                }
            }
            AppHost host = this.ResolveService<AppHost>();
            host.RemoveEvent(request.ListenerGuid, request.DataCollectorGuid);
            return new GXEventsUnregisterResponse();
        }

        public GXEventsRegisterResponse Post(GXEventsRegisterRequest request)
        {
            if (request.Actions == Actions.None && request.Targets == ActionTargets.None)
            {
                return new GXEventsRegisterResponse();
            }
            IAuthSession s = this.GetSession(false);
            long id = 0;
            bool superAdmin = false;
            Guid guid = request.DataCollectorGuid;
            //Guid is set if DC is retreaving new tasks.
            if (long.TryParse(s.Id, out id))
            {
                superAdmin = GuruxAMI.Server.GXBasicAuthProvider.IsSuperAdmin(s);
            }
            ulong mask = (ulong)(((int)request.Targets << 16) | (int)request.Actions);
            AppHost host = this.ResolveService<AppHost>();
            GXEvent e1 = new GXEvent(id, superAdmin, guid, mask);
            host.AddEvent(request.ListenerGuid, e1);
            return new GXEventsRegisterResponse();
        }

        /// <summary>
        /// Wait until new event is added and return it.
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        public GXEventsResponse Get(GXEventsRequest request)
		{
            IAuthSession s = this.GetSession(false);
            long id = 0;
            bool superAdmin = false;
            Guid guid;
            //Guid is set if DC is retreaving new tasks.
            if (long.TryParse(s.Id, out id))
            {
                superAdmin = GuruxAMI.Server.GXBasicAuthProvider.IsSuperAdmin(s);
            }            
            AppHost host = this.ResolveService<AppHost>();
            GXEventsItem[] events = host.WaitEvents(request.ListenerGuid, out guid);            
            return new GXEventsResponse(events, guid);
		}
	}
}
