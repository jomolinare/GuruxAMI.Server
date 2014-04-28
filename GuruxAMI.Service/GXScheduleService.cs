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
using System.Linq;
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
    /// Service handles Schedule functionality.
    /// </summary>
	[Authenticate]
#if !SS4
    internal class GXScheduleService : ServiceStack.ServiceInterface.Service
#else
    internal class GXScheduleService : ServiceStack.Service
#endif
	{
        /// <summary>
        /// Schedule is added or edit.
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        public GXScheduleUpdateResponse Post(GXScheduleUpdateRequest request)
		{
            List<GXEventsItem> events = new List<GXEventsItem>();
            IAuthSession s = this.GetSession(false);
            long adderId = Convert.ToInt64(s.Id);
            lock (Db)
            {
                using (var trans = Db.OpenTransaction(IsolationLevel.ReadCommitted))
                {
                    //Add new schedules.
                    foreach (GXAmiSchedule it in request.Schedules)
                    {
                        if (string.IsNullOrEmpty(it.Name))
                        {
                            throw new ArgumentException("Invalid name.");
                        }
                        //If new schedule.
                        if (it.Id == 0)
                        {
                            //All date times are saved to DB in universal time.
                            it.Added = DateTime.Now.ToUniversalTime();
                            if (it.TransactionStartTime != null)
                            {
                                it.TransactionStartTime = it.TransactionStartTime.Value.ToUniversalTime();
                            }
                            if (it.TransactionEndTime != null)
                            {
                                it.TransactionEndTime = it.TransactionEndTime.Value.ToUniversalTime();
                            }
                            if (it.ScheduleStartTime != null)
                            {
                                it.ScheduleStartTime = it.ScheduleStartTime.Value.ToUniversalTime();
                            }
                            if (it.ScheduleEndTime != null)
                            {
                                it.ScheduleEndTime = it.ScheduleEndTime.Value.ToUniversalTime();
                            }
                            if (it.NextRunTine != null)
                            {
                                it.NextRunTine = it.NextRunTine.Value.ToUniversalTime();
                            }
                            if (it.LastRunTime != null)
                            {
                                it.LastRunTime = it.LastRunTime.Value.ToUniversalTime();
                            }
                            Db.Insert(it);
#if !SS4
                            it.Id = (ulong) Db.GetLastInsertId();
#else
                            it.Id = Db.LastInsertId();
#endif
                            //Restore date times back to local time.
                            it.Added = it.Added.ToLocalTime();
                            if (it.TransactionStartTime != null)
                            {
                                it.TransactionStartTime = it.TransactionStartTime.Value.ToLocalTime();
                            }
                            if (it.TransactionEndTime != null)
                            {
                                it.TransactionEndTime = it.TransactionEndTime.Value.ToLocalTime();
                            }
                            if (it.ScheduleStartTime != null)
                            {
                                it.ScheduleStartTime = it.ScheduleStartTime.Value.ToLocalTime();
                            }
                            if (it.ScheduleEndTime != null)
                            {
                                it.ScheduleEndTime = it.ScheduleEndTime.Value.ToLocalTime();
                            }
                            if (it.NextRunTine != null)
                            {
                                it.NextRunTine = it.NextRunTine.Value.ToLocalTime();
                            }
                            if (it.LastRunTime != null)
                            {
                                it.LastRunTime = it.LastRunTime.Value.ToLocalTime();
                            }                           
                            events.Add(new GXEventsItem(ActionTargets.Schedule, Actions.Add, it));
                        }
                        else //Update schedule.
                        {
                            //Get Added time.
#if !SS4
                            GXAmiSchedule orig = Db.GetById<GXAmiSchedule>(it.Id);
#else
                            GXAmiSchedule orig = Db.SingleById<GXAmiSchedule>(it.Id);
#endif
                            it.Added = orig.Added;
                            it.TransactionStartTime = orig.TransactionStartTime;
                            it.TransactionEndTime = orig.TransactionEndTime;
                            it.ScheduleStartTime = orig.ScheduleStartTime;
                            it.ScheduleEndTime = orig.ScheduleEndTime;
                            it.NextRunTine = orig.NextRunTine;
                            it.LastRunTime = orig.LastRunTime;
                            Db.Update(it);
                            events.Add(new GXEventsItem(ActionTargets.Schedule, Actions.Edit, it));
                        }
                        //Delete all targets first.
                        Db.Delete<GXAmiScheduleTarget>(q => q.ScheduleId == it.Id);
                        //Add targets.
                        foreach (GXAmiScheduleTarget t in it.Targets)
                        {
                            t.ScheduleId = it.Id;
                            Db.Insert<GXAmiScheduleTarget>(t);
                        }
                        
                    }
                    trans.Commit();
                }
            }
            AppHost host = this.ResolveService<AppHost>();
            host.SetEvents(Db, this.Request, adderId, events);
            return new GXScheduleUpdateResponse(request.Schedules);
		}

        /// <summary>
        /// Get schedules.
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
		public GXScheduleResponse Post(GXScheduleRequest request)
		{
            List<GXAmiSchedule> list = null;
            IAuthSession s = this.GetSession(false);            
            bool superAdmin = GuruxAMI.Server.GXBasicAuthProvider.IsSuperAdmin(s);
            lock (Db)
            {                
                //Returns the schedules that users can handle.
                if (request.UserIDs != null && request.UserIDs.Length != 0)
                {
                    //TODO:
                }
                //Returns the schedules that user groups can handle.
                else if (request.UserGroupIDs != null && request.UserGroupIDs.Length != 0)
                {
                    //TODO:
                }
                else//Get all schedules that use can access.
                {
                    //Return all schedules.
                    if (superAdmin)
                    {
                        list = Db.Select<GXAmiSchedule>();
                    }
                    else//Return only shedule that target devices user can access.
                    {
                        //TODO:
                        list = Db.Select<GXAmiSchedule>();
                    }
                }

                //Remove excluded schedules.
                if (request.Excluded != null && request.Excluded.Length != 0)
                {                    
                    List<ulong> ids = new List<ulong>(request.Excluded);
                    var excludeSchedules = from c in list where !ids.Contains(c.Id) select c;
                    list = excludeSchedules.ToList();                 
                }
                //Get schedules by range.
                if (request.Index != 0 || request.Count != 0)
                {
                    if (request.Count == 0 || request.Index + request.Count > list.Count)
                    {
                        request.Count = list.Count - request.Index;
                    }
                    list.RemoveRange(0, request.Index);
                    var limitschedules = list.Take(request.Count);
                    list = limitschedules.ToList();
                }                
            }
            foreach(GXAmiSchedule it in list)
            {                
                if (it.TransactionStartTime != null)
                {
                    it.TransactionStartTime = DateTime.SpecifyKind(it.TransactionStartTime.Value, DateTimeKind.Utc);
                }                
                if (it.TransactionEndTime != null)
                {
                    it.TransactionEndTime = DateTime.SpecifyKind(it.TransactionEndTime.Value, DateTimeKind.Utc);
                }
                if (it.ScheduleStartTime != null)
                {
                    it.ScheduleStartTime = DateTime.SpecifyKind(it.ScheduleStartTime.Value, DateTimeKind.Utc);
                }
                if (it.ScheduleEndTime != null)
                {
                    it.ScheduleEndTime = DateTime.SpecifyKind(it.ScheduleEndTime.Value, DateTimeKind.Utc);
                }
                if (it.NextRunTine != null)
                {
                    it.NextRunTine = DateTime.SpecifyKind(it.NextRunTine.Value, DateTimeKind.Utc);
                }
                if (it.LastRunTime != null)
                {
                    it.LastRunTime = DateTime.SpecifyKind(it.LastRunTime.Value, DateTimeKind.Utc);
                }
                //Get targets.
                it.Targets = Db.Select<GXAmiScheduleTarget>(q => q.ScheduleId == it.Id).ToArray();
            }
            return new GXScheduleResponse(list.ToArray());
		}

        /// <summary>
        /// Selete selected schedules.
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
		public GXScheduleDeleteResponse Post(GXScheduleDeleteRequest request)
		{
            IAuthSession s = this.GetSession(false);
            int id = Convert.ToInt32(s.Id);
            if (id == 0)
            {
                throw new ArgumentException("Remove failed. Invalid session ID.");
            }
            if (!GuruxAMI.Server.GXBasicAuthProvider.CanUserEdit(s))
            {
                throw new ArgumentException("Remove not allowed.");
            }
            List<GXEventsItem> events = new List<GXEventsItem>();
            bool superAdmin = GuruxAMI.Server.GXBasicAuthProvider.IsSuperAdmin(s);
            lock (Db)
            {
                foreach (ulong it in request.Schedules)
                {
                    if (it == 0)
                    {
                        throw new ArgumentException("ID is required");
                    }                    

                    //Only super admin or creator can remove schedule.
                    //TODO:
#if !SS4
                    GXAmiSchedule schedule = Db.QueryById<GXAmiSchedule>(it);
#else
                    GXAmiSchedule schedule = Db.SingleById<GXAmiSchedule>(it);
#endif
                    if (request.Permanently)
                    {
                        Db.DeleteById<GXAmiSchedule>(it);
                    }
                    else
                    {
                        schedule.Removed = DateTime.Now.ToUniversalTime();
                        Db.UpdateOnly(schedule, p => p.Removed, p => p.Id == it);
                    }
                    events.Add(new GXEventsItem(ActionTargets.Schedule, Actions.Remove, schedule));
                }
            }
            AppHost host = this.ResolveService<AppHost>();
            host.SetEvents(Db, this.Request, id, events);            
			return new GXScheduleDeleteResponse();
		}

        /// <summary>
        /// Start, Stop or run selected schedules.
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        public GXScheduleActionResponse Post(GXScheduleActionRequest request)
        {
            //TODO: Check that use has access to the schedule.            
            List<GXEventsItem> events = new List<GXEventsItem>();
            List<GXAmiSchedule> list = new List<GXAmiSchedule>();
            IAuthSession s = this.GetSession(false);
            long userId = Convert.ToInt64(s.Id);            

            if (request.Action != Gurux.Device.ScheduleState.Run &&
                request.Action != Gurux.Device.ScheduleState.Start &&
                request.Action != Gurux.Device.ScheduleState.TaskStart &&
                request.Action != Gurux.Device.ScheduleState.TaskFinish &&
                request.Action != Gurux.Device.ScheduleState.End)
            {
                throw new ArgumentOutOfRangeException("Schedule execution failed.");
            }
            lock (Db)
            {
                foreach (ulong it in request.ScheduleIDs)
                {
                    list = Db.Select<GXAmiSchedule>(q => q.Id == it);
                    if (list.Count != 1)
                    {
                        throw new Exception("Unknown schedule.");
                    }
                    if (request.Action == Gurux.Device.ScheduleState.Start)
                    {
                        list[0].Status = Gurux.Device.ScheduleState.Run;
                    }
                    else if (request.Action == Gurux.Device.ScheduleState.TaskStart)
                    {
                        list[0].Status |= Gurux.Device.ScheduleState.TaskRun;
                    }
                    else if (request.Action == Gurux.Device.ScheduleState.TaskFinish)
                    {
                        list[0].Status &= ~Gurux.Device.ScheduleState.TaskRun;
                    }
                    else if (request.Action == Gurux.Device.ScheduleState.End)
                    {
                        list[0].Status = Gurux.Device.ScheduleState.None;
                    }
                    else
                    {
                        System.Diagnostics.Debug.Assert(false);
                    }
                    
                    events.Add(new GXEventsItem(ActionTargets.Schedule, Actions.State, list[0]));
                }
            }
            AppHost host = this.ResolveService<AppHost>();
            host.SetEvents(Db, this.Request, userId, events);
            return new GXScheduleActionResponse();
        }        
	}
}
