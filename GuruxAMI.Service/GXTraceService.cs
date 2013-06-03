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

using GuruxAMI.Common.Messages;
using ServiceStack.ServiceInterface;
using GuruxAMI.Common;
using System.Collections.Generic;
using System.Data;
using ServiceStack.OrmLite;
using System;
using System.Xml.Linq;
using ServiceStack.ServiceInterface.Auth;
using GuruxAMI.Server;
using System.Text;

namespace GuruxAMI.Service
{
    /// <summary>
    /// Service handles Task functionality.
    /// </summary>
    [Authenticate]
    internal class GXTraceService : ServiceStack.ServiceInterface.Service
    {
        /// <summary>
        /// Can user, add or remove users.
        /// </summary>
        /// <param name="s"></param>
        /// <returns></returns>
        static public bool IsLimitedAccess(IAuthSession s)
        {
            int value = Convert.ToInt32(s.Roles[0]);
            return (value & (int)UserAccessRights.LimitedAccess) == (int)UserAccessRights.LimitedAccess;
        }

        /// <summary>
        /// Update trace level for selected devices or Data Collectors.
        /// </summary>
        public GXTraceUpdateResponse Post(GXTraceUpdateRequest request)
        {
            List<GXEventsItem> events = new List<GXEventsItem>();
            if (request.DataCollectorIDs != null)
            {
                foreach (ulong id in request.DataCollectorIDs)
                {
                    GXAmiDataCollector it = Db.GetById<GXAmiDataCollector>(id);
                    it.TraceLevel = request.Level;
                    Db.UpdateOnly(it, p => p.TraceLevelAsInt, p => p.Id == id);
                    events.Add(new GXEventsItem(ActionTargets.Trace, Actions.Edit, it));
                }
            }
            if (request.DeviceIDs != null)
            {
                foreach (ulong id in request.DeviceIDs)
                {
                    GXAmiDevice it = Db.GetById<GXAmiDevice>(id);
                    it.TraceLevel = request.Level;
                    Db.UpdateOnly(it, p => p.TraceLevelAsInt, p => p.Id == id);
                    events.Add(new GXEventsItem(ActionTargets.Trace, Actions.Edit, it));
                }
            }
            AppHost host = this.ResolveService<AppHost>();
            host.SetEvents(Db, this.Request, 0, events);
            return new GXTraceUpdateResponse();
        }

        /// <summary>
        /// Data Collector or devices add new trace. Traces can't update.
        /// </summary>
        public GXTraceAddResponse Post(GXTraceAddRequest request)
        {
            List<GXEventsItem> events = new List<GXEventsItem>();
            foreach (GXAmiTrace it in request.Traces)
            {
                //If DC is adding trace.
                if (it.DeviceId == 0)
                {
                    IAuthSession s = this.GetSession(false);
                    Guid guid;
                    if (!GuruxAMI.Server.GXBasicAuthProvider.IsGuid(s.Id, out guid))
                    {
                        throw new ArgumentException("Access denied.");
                    }
                    List<GXAmiDataCollector> list = Db.Select<GXAmiDataCollector>(q => q.Guid == guid);
                    if (list.Count != 1)
                    {
                        throw new ArgumentException("Access denied.");
                    }
                    it.DataCollectorId = list[0].Id;
                    it.DataCollectorGuid = guid;
                }
                it.Timestamp = DateTime.Now;
                Db.Insert(it);
                it.Id = (ulong) Db.GetLastInsertId();
                it.DataType = it.Data.GetType().FullName;
                string data;
                if (it.Data is byte[])
                {
                    data = BitConverter.ToString(it.Data as byte[]);
                }
                else
                {
                    data = it.Data.ToString();
                }
                string[] tmp = SplitByLength(data, 255);
                int index = 0;
                //If there is only one row set index to 0, otherwice start index from One.
                if (tmp.Length == 1)
                {
                    index = -1;
                }
                foreach (string str in tmp)
                {
                    GXAmiTraceData d = new GXAmiTraceData();
                    d.Index = ++index;
                    d.TraceId = it.Id;
                    d.Data = str;
                    Db.Insert(d);
                }
                events.Add(new GXEventsItem(ActionTargets.Trace, Actions.Add, it));
            }
            AppHost host = this.ResolveService<AppHost>();
            host.SetEvents(Db, this.Request, 0, events);
            return new GXTraceAddResponse();
        }

        static string[] SplitByLength(string str, int maxLength)
        {
            int cnt = str.Length / maxLength;
            if (str.Length % maxLength != 0)
            {
                ++cnt;
            }
            List<string> items = new List<string>(cnt);
            for (int index = 0; index < str.Length; index += maxLength)
            {
                items.Add(str.Substring(index, Math.Min(maxLength, str.Length - index)));
            }
            return items.ToArray();
        }

        /// <summary>
        /// Get trace level state.
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        public GXTraceLevelResponse Get(GXTraceLevelRequest request)
        {
            List<System.Diagnostics.TraceLevel> list = new List<System.Diagnostics.TraceLevel>();
            if (request.DataCollectors != null)
            {
                foreach (Guid guid in request.DataCollectors)
                {
                    GXAmiDataCollector it = Db.Select<GXAmiDataCollector>(q => q.Guid == guid)[0];
                    list.Add(it.TraceLevel);
                }
            }
            if (request.DeviceIDs != null)
            {
                foreach (ulong id in request.DeviceIDs)
                {
                    GXAmiDevice it = Db.Select<GXAmiDevice>(q => q.Id == id)[0];
                    list.Add(it.TraceLevel);
                }
            }
            return new GXTraceLevelResponse(list.ToArray());            
        }       

        /// <summary>
        /// Get traces.
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        public GXTracesResponse Get(GXTracesRequest request)
        {
            List<GXAmiTrace> list = new List<GXAmiTrace>();
            if (request.DataCollectors != null)
            {
                foreach (Guid guid in request.DataCollectors)
                {
                    string query = string.Format("SELECT {0}.* FROM {0} INNER JOIN {1} ON {0}.DataCollectorId = {1}.ID WHERE {1}.Guid = '{2}'",
                        GuruxAMI.Server.AppHost.GetTableName<GXAmiTrace>(Db),
                        GuruxAMI.Server.AppHost.GetTableName<GXAmiDataCollector>(Db),
                        guid.ToString().Replace("-", ""));
                    list.AddRange(Db.Select<GXAmiTrace>(query));
                }
            }
            if (request.DeviceIDs != null)
            {
                foreach (ulong id in request.DeviceIDs)
                {
                    list.AddRange(Db.Select<GXAmiTrace>(q => q.DeviceId == id));
                }
            }
            return new GXTracesResponse(list.ToArray());
        }    

        /// <summary>
        /// Delete traces from the device or Data Collector.
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        public GXTraceDeleteResponse Delete(GXTraceDeleteRequest request)
        {
            List<GXEventsItem> events = new List<GXEventsItem>();
            List<GXAmiTrace> items = new List<GXAmiTrace>();
            if (request.DeviceIDs != null)
            {
                foreach (ulong id in request.DeviceIDs)
                {
                    //TODO: Check that user can access the device.
                    items.AddRange(Db.Select<GXAmiTrace>(q => q.DeviceId == id));
                }
            }
            if (request.DataCollectors != null)
            {
                foreach (Guid guid in request.DataCollectors)
                {
                    //TODO: Check that user can access the DC.
                    string query = string.Format("SELECT {0}.* FROM {0} INNER JOIN {1} ON {0}.DataCollectorId = {1}.ID WHERE {1}.Guid = {2}",
                        GuruxAMI.Server.AppHost.GetTableName<GXAmiTrace>(Db),
                        GuruxAMI.Server.AppHost.GetTableName<GXAmiDataCollector>(Db),
                        guid.ToString().Replace("-", ""));
                    items.AddRange(Db.Select<GXAmiTrace>(query));
                }
            }
            if (request.TraceIDs != null)
            {
                foreach (ulong id in request.TraceIDs)
                {
                    //TODO: Check that user can access the DC.
                    items.AddRange(Db.Select<GXAmiTrace>(q => q.Id == id));
                }
            }
            Db.DeleteAll(items.ToArray());
            foreach (GXAmiTrace it in items)
            {
                events.Add(new GXEventsItem(ActionTargets.Trace, Actions.Add, it));
            }
            AppHost host = this.ResolveService<AppHost>();
            host.SetEvents(Db, this.Request, 0, events);
            return new GXTraceDeleteResponse();
        }
    }
}
