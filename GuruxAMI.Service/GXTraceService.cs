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
using GuruxAMI.Common;
using System.Collections.Generic;
using System.Data;
using ServiceStack.OrmLite;
using System;
using System.Xml.Linq;
using GuruxAMI.Server;
using System.Text;

#if !SS4
using ServiceStack.ServiceInterface;
using ServiceStack.ServiceInterface.Auth;
#else
using ServiceStack;
using ServiceStack.Auth;
#endif

namespace GuruxAMI.Service
{
    /// <summary>
    /// Service handles trace functionality.
    /// </summary>
    [Authenticate]
    internal class GXTraceService : GXService
    {        
        /// <summary>
        /// Update trace level for selected devices or Data Collectors.
        /// </summary>
        public GXTraceUpdateResponse Post(GXTraceUpdateRequest request)
        {
            List<GXEventsItem> events = new List<GXEventsItem>();
            lock (Db)
            {
                if (request.DataCollectorIDs != null)
                {
                    foreach (ulong id in request.DataCollectorIDs)
                    {
#if !SS4
                        GXAmiDataCollector it = Db.GetById<GXAmiDataCollector>(id);
#else
                        GXAmiDataCollector it = Db.SingleById<GXAmiDataCollector>(id);
#endif
                        it.TraceLevel = request.Level;
                        Db.UpdateOnly(it, p => p.TraceLevelAsInt, p => p.Id == id);
                        events.Add(new GXEventsItem(ActionTargets.Trace, Actions.Edit, it));
                    }
                }
                if (request.DeviceIDs != null)
                {
                    foreach (ulong id in request.DeviceIDs)
                    {
#if !SS4
                        GXAmiDevice it = Db.GetById<GXAmiDevice>(id);
#else
                        GXAmiDevice it = Db.SingleById<GXAmiDevice>(id);
#endif                       
                        it.TraceLevel = request.Level;
                        Db.UpdateOnly(it, p => p.TraceLevelAsInt, p => p.Id == id);
                        events.Add(new GXEventsItem(ActionTargets.Trace, Actions.Edit, it));
                    }
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
            lock (Db)
            {
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
#if !SS4
                    it.Id = (ulong)Db.GetLastInsertId();
#else
                    it.Id = (ulong)Db.LastInsertId();
#endif                    
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
            lock (Db)
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
        }       

        /// <summary>
        /// Get traces.
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        public GXTracesResponse Post(GXTracesRequest request)
        {
            lock (Db)
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
        }    

        /// <summary>
        /// Delete traces from the device or Data Collector.
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        public GXTraceDeleteResponse Post(GXTraceDeleteRequest request)
        {
            List<GXEventsItem> events = new List<GXEventsItem>();
            List<GXAmiTrace> items = new List<GXAmiTrace>();
            lock (Db)
            {
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
                Db.DeleteByIds<GXAmiTrace>(items.ToArray());
                foreach (GXAmiTrace it in items)
                {
                    events.Add(new GXEventsItem(ActionTargets.Trace, Actions.Add, it));
                }
            }
            AppHost host = this.ResolveService<AppHost>();
            host.SetEvents(Db, this.Request, 0, events);
            return new GXTraceDeleteResponse();
        }
    }
}
