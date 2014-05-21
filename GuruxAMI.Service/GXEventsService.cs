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
using System.Reflection;
using ServiceStack.OrmLite;
using System.Data;
using GuruxAMI.Common;
using System.Collections.Generic;
using System.Threading;
using System;
using GuruxAMI.Common.Messages;
using GuruxAMI.Server;
#if !SS4
using ServiceStack.ServiceInterface;
using ServiceStack.ServiceInterface.Auth;
using ServiceStack.WebHost.Endpoints;
using ServiceStack.ServiceHost;
#else
using ServiceStack;
using ServiceStack.Auth;
#endif

namespace GuruxAMI.Service
{
    /// <summary>
    /// Service handles event functionality.
    /// </summary>
	[Authenticate]
    internal class GXEventsService : GXService
	{
        /// <summary>
        /// Stop listen events.
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        public GXEventsUnregisterResponse Put(GXEventsUnregisterRequest request)
        {            
            if (request.Instance.Equals(Guid.Empty))
            {
                throw new Exception("Guid is empty.");
            }        
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
            host.RemoveEvent(request.Instance, request.DataCollectorGuid);
            if (guid != Guid.Empty)
            {
                //Notify that DC is disconnected.
                List<GXEventsItem> events = new List<GXEventsItem>();
                lock (Db)
                {
                    GXAmiDataCollector dc = Db.Select<GXAmiDataCollector>(q => q.Guid == guid)[0];
                    dc.State = Gurux.Device.DeviceStates.None;
                    Db.UpdateOnly(dc, p => p.StatesAsInt, p => p.Id == dc.Id);
                    events.Add(new GXEventsItem(ActionTargets.DataCollector, Actions.State, dc));
                }                                
                host.SetEvents(Db, this.Request, 0, events);
            }
            return new GXEventsUnregisterResponse();
        }

        /// <summary>
        /// Start listen events.
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        public GXEventsRegisterResponse Post(GXEventsRegisterRequest request)
        {
            if (request.SessionListener.Equals(Guid.Empty) ||
                request.Instance.Equals(Guid.Empty))
            {
                throw new Exception("Listener Guid is empty.");
            }
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
            else
            {
                if (request.DataCollectorGuid.Equals(Guid.Empty))
                {
                    throw new Exception("Data collector Guid is empty.");
                }
            }
            AppHost host = this.ResolveService<AppHost>();
            //Check that there are no several DCs with same Guid.
            //Note! This might happend when DC is restarted wrong.
            //For this reason we are only give a warning.
            List<GXEventsItem> events = new List<GXEventsItem>();
            if (host.IsDCRegistered(guid))
            {
                lock (Db)
                {
                    GXAmiSystemError e = new GXAmiSystemError(1, ActionTargets.SystemError, Actions.State, new Exception("Data collector already exists."));
                    Db.Insert<GXAmiSystemError>(e);
                    events.Add(new GXEventsItem(ActionTargets.SystemError, Actions.Add, e));
                }
            }
            ulong mask = (ulong)(((int)request.Targets << 16) | (int)request.Actions);
            GXEvent e1 = new GXEvent(id, superAdmin, guid, request.Instance, mask);
            host.AddEvent(request.SessionListener, e1);
            if (guid != Guid.Empty)
            {
                //Notify that DC is connected.
                lock (Db)
                {
                    GXAmiDataCollector dc = Db.Select<GXAmiDataCollector>(q => q.Guid == guid)[0];
                    dc.State = Gurux.Device.DeviceStates.Connected;
                    Db.UpdateOnly(dc, p => p.StatesAsInt, p => p.Id == dc.Id);
                    events.Add(new GXEventsItem(ActionTargets.DataCollector, Actions.State, dc));
                }
                host.SetEvents(Db, this.Request, 0, events);
            }
            return new GXEventsRegisterResponse();
        }

        /// <summary>
        /// Wait until new event is added and return it.
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        public GXEventsResponse Post(GXEventsRequest request)
		{
            if (Guid.Empty.Equals(request.Instance))
            {
                throw new Exception("Guid is empty.");             
            }            
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
            GXEventsItem[] events = host.WaitEvents(Db, request.Instance, out guid);
            return new GXEventsResponse(events, guid);
		}
	}
}
