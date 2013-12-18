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
using System.Collections.Generic;
using System.Data;
using GuruxAMI.Common;
using GuruxAMI.Common.Messages;
using ServiceStack.OrmLite;
using ServiceStack.ServiceInterface;
using ServiceStack.ServiceInterface.Auth;
using GuruxAMI.Server;
using System.Linq;

namespace GuruxAMI.Service
{
    /// <summary>
    /// Service handles device functionality.
    /// </summary>
	[Authenticate]
    internal class GXDeviceService : ServiceStack.ServiceInterface.Service
	{
        void UpdateParameters(IDbConnection Db, ulong deviceId, ulong parentId, GXAmiParameter[] parameters, bool insert)
        {
            if (parameters != null)
            {
                foreach (GXAmiParameter it in parameters)
                {
                    if (insert || it.Id == 0)
                    {
                        GXAmiParameter tmp = it;
                        if (it is GXAmiParameterTemplate)
                        {
                            tmp = (it as GXAmiParameterTemplate).ToParameter();
                        }
                        tmp.DeviceID = deviceId;
                        tmp.ParentID = parentId;
                        Db.Insert<GXAmiParameter>(tmp);
                    }
                    else
                    {
                        //User can only change value of the parameter.
                        Db.UpdateOnly(it, p => p.Value, p => p.Id == it.Id);
                    }
                }
            }
        }

        /// <summary>
        /// Update paraeters.
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        public GXParameterUpdateResponse Post(GXParameterUpdateRequest request)
        {
            using (IDbTransaction trans = Db.OpenTransaction(IsolationLevel.ReadCommitted))
            {
                foreach (var it in request.Parameters)
                {
                    GXAmiParameter param = new GXAmiParameter();
                    param.Value = it.Value;
                    Db.UpdateOnly(param, p => p.Value, p => p.Id == it.Key);
                }
                trans.Commit();
            }
            return new GXParameterUpdateResponse();
        }

        /// <summary>
        /// Add or update device.
        /// </summary>
        public GXDeviceUpdateResponse Put(GXDeviceUpdateRequest request)
        {
            List<GXEventsItem> events = new List<GXEventsItem>();
            IAuthSession s = this.GetSession(false);
            //Normal user can't change device group name or add new one.
            if (!GuruxAMI.Server.GXBasicAuthProvider.CanUserEdit(s))
            {
                throw new ArgumentException("Access denied.");
            }
            long adderId = Convert.ToInt64(s.Id);
            using (var trans = Db.OpenTransaction(IsolationLevel.ReadCommitted))
            {
                bool superAdmin = GuruxAMI.Server.GXBasicAuthProvider.IsSuperAdmin(s);
                //Add new device groups           
                foreach (GXAmiDevice it in request.Devices)
                {
                    if (string.IsNullOrEmpty(it.Name))
                    {
                        throw new ArgumentException("Invalid name.");
                    }
                    //If new device.
                    if (it.Id == 0)
                    {
                        it.Id = GXAmiSettings.GetNewDeviceID(Db);
                        it.Added = DateTime.Now.ToUniversalTime();
                        Db.Insert(it);
                        events.Add(new GXEventsItem(ActionTargets.Device, Actions.Add, it));
                        //Add adder to user group if adder is not super admin.
                        foreach (ulong dgId in request.DeviceGroups)
                        {
                            //Can user access to the device group.
                            if (!superAdmin && dgId != 0 && !GXDeviceGroupService.CanUserAccessDeviceGroup(Db, adderId, dgId))
                            {
                                throw new ArgumentException("Access denied.");
                            }
                            GXAmiDeviceGroupDevice g = new GXAmiDeviceGroupDevice();
                            g.DeviceGroupID = dgId;
                            g.DeviceID = it.Id;
                            g.Added = DateTime.Now.ToUniversalTime();
                            Db.Insert(g);
                            events.Add(new GXEventsItem(ActionTargets.DeviceGroup, Actions.Edit, dgId));
                        }
                    }
                    else //Update device.
                    {
                        if (request.DeviceGroups != null)
                        {
                            foreach (ulong dgId in request.DeviceGroups)
                            {
                                //Can user access to the device group.
                                if (!superAdmin && !GXDeviceGroupService.CanUserAccessDeviceGroup(Db, adderId, dgId))
                                {
                                    throw new ArgumentException("Access denied.");
                                }
                                if (dgId == 0)
                                {
                                    GXAmiDeviceGroupDevice g = new GXAmiDeviceGroupDevice();
                                    g.DeviceGroupID = dgId;
                                    g.DeviceID = it.Id;
                                    g.Added = DateTime.Now.ToUniversalTime();
                                    Db.Insert(g);
                                    events.Add(new GXEventsItem(ActionTargets.DeviceGroup, Actions.Edit, dgId));
                                }
                            }
                        }
                        //Get Added time.
                        GXAmiDevice orig = Db.GetById<GXAmiDevice>(it.Id);
                        it.Added = orig.Added;
                        Db.Update(it);
                        events.Add(new GXEventsItem(ActionTargets.Device, Actions.Edit, it));
                    }                    
                    //Bind user groups to the device groups.
                    if (request.DeviceGroups != null)
                    {
                        foreach (ulong dgId in request.DeviceGroups)
                        {
                            string query = string.Format("SELECT DeviceID FROM " +
                                GuruxAMI.Server.AppHost.GetTableName<GXAmiDeviceGroupDevice>(Db) +
                                "WHERE DeviceID = {0} AND DeviceGroupID = {1}", it.Id, dgId);
                            if (Db.Select<GXAmiUserGroupDeviceGroup>(query).Count == 0)
                            {
                                GXAmiDeviceGroupDevice item = new GXAmiDeviceGroupDevice();
                                item.DeviceGroupID = dgId;
                                item.DeviceID = it.Id;
                                item.Added = DateTime.Now.ToUniversalTime();
                                Db.Insert<GXAmiUserGroupDeviceGroup>();
                                events.Add(new GXEventsItem(ActionTargets.Device, Actions.Edit, it));
                                events.Add(new GXEventsItem(ActionTargets.DeviceGroup, Actions.Edit, dgId));
                            }
                        }
                    }
                    ///////////////////////////////////////////////
                    //Update device parameters.
                    UpdateParameters(Db, it.Id, it.Id, it.Parameters, false);
                    
                    ///////////////////////////////////////////////
                    //Update categories
                    if (it.Categories == null)
                    {
                        GXAmiCategoryTemplate[] tmp22 = Db.Select<GXAmiCategoryTemplate>(q => q.DeviceID == it.TemplateId).ToArray();                        
                        List<GXAmiCategory> categories = new List<GXAmiCategory>();
                        foreach (GXAmiCategoryTemplate tmp in tmp22)
                        {
                            GXAmiCategory cat = tmp.ToCategory();
                            cat.DeviceID = it.Id;
                            Db.Insert(cat);
                            categories.Add(cat);
                            cat.Parameters = Db.Select<GXAmiParameterTemplate>(q => q.ParentID == tmp.Id).ToArray();
                            UpdateParameters(Db, it.Id, cat.Id, cat.Parameters, true);
                            GXAmiPropertyTemplate[] tmp23 = Db.Select<GXAmiPropertyTemplate>(q => q.ParentID == tmp.Id).ToArray();
                            List<GXAmiProperty> properties = new List<GXAmiProperty>();
                            foreach (GXAmiPropertyTemplate tmp2 in tmp23)
                            {
                                GXAmiProperty p = tmp2.ToProperty();
                                p.ParentID = cat.Id;
                                p.DeviceID = it.Id;
                                Db.Insert(p);
                                p.Parameters = Db.Select<GXAmiParameterTemplate>(q => q.ParentID == tmp2.Id).ToArray();
                                UpdateParameters(Db, it.Id, p.Id, p.Parameters, true);
                                properties.Add(p);
                            }
                            cat.Properties = properties.ToArray();
                        }
                        it.Categories = categories.ToArray();
                    }
                    else
                    {
                        foreach (GXAmiCategory cat in it.Categories)
                        {
                            //User can change category so it is not updated. Db.Update(cat);                            
                            //Update category parameters.
                            UpdateParameters(Db, cat.Id, cat.Id, cat.Parameters, false);
                            //Update properties
                            foreach (GXAmiProperty p in cat.Properties)
                            {
                                //User can change property so it is not updated. Db.Update(p);
                                //Update property parameters.
                                UpdateParameters(Db, p.Id, p.Id, p.Parameters, false);
                            }
                        }
                    }
                    ///////////////////////////////////////////////
                    //Update tables
                    if (it.Tables == null)
                    {
                        GXAmiTableTemplate[] tmp22 = Db.Select<GXAmiTableTemplate>(q => q.DeviceID == it.TemplateId).ToArray();
                        List<GXAmiDataTable> tables = new List<GXAmiDataTable>();
                        foreach (GXAmiTableTemplate tmp in tmp22)
                        {
                            GXAmiDataTable table = tmp.ToTable();
                            table.DeviceID = it.Id;
                            Db.Insert(table);
                            tables.Add(table);
                            table.Parameters = Db.Select<GXAmiParameterTemplate>(q => q.ParentID == tmp.Id).ToArray();
                            UpdateParameters(Db, it.Id, table.Id, table.Parameters, true);
                            GXAmiPropertyTemplate[] tmp23 = Db.Select<GXAmiPropertyTemplate>(q => q.ParentID == tmp.Id).ToArray();
                            List<GXAmiProperty> properties = new List<GXAmiProperty>();
                            foreach (GXAmiPropertyTemplate tmp2 in tmp23)
                            {
                                GXAmiProperty p = tmp2.ToProperty();
                                p.ParentID = table.Id;
                                p.DeviceID = it.Id;
                                Db.Insert(p);
                                p.Parameters = Db.Select<GXAmiParameterTemplate>(q => q.ParentID == tmp2.Id).ToArray();
                                UpdateParameters(Db, it.Id, p.Id, p.Parameters, true);
                                properties.Add(p);
                            }
                            table.Columns = properties.ToArray();
                        }
                        it.Tables = tables.ToArray();
                    }
                    else
                    {
                        foreach (GXAmiDataTable table in it.Tables)
                        {
                            //User can change table so it is not updated. Db.Update(table);
                            //Update category parameters.
                            UpdateParameters(Db, table.Id, table.Id, table.Parameters, false);
                            //Update properties
                            foreach (GXAmiProperty p in table.Columns)
                            {
                                //User can change category so it is not updated. Db.Update(p);
                                //Update property parameters.
                                UpdateParameters(Db, p.Id, p.Id, p.Parameters, false);
                            }
                        }
                    }
                }
                trans.Commit();
                AppHost host = this.ResolveService<AppHost>();
                host.SetEvents(Db, this.Request, adderId, events);
            }
            return new GXDeviceUpdateResponse(request.Devices);
        }

        static public List<GXAmiDevice> GetDevices(IAuthSession s, IDbConnection Db, long userId, long userGroupId, ulong deviceGroupId, ulong deviceID, bool removed)
        {
            string id = s.Id;
            bool admin = GuruxAMI.Server.GXBasicAuthProvider.IsSuperAdmin(s);
            List<string> Filter = new List<string>();
            string query = string.Format("SELECT DISTINCT {0}.* FROM {0} ", GuruxAMI.Server.AppHost.GetTableName<GXAmiDevice>(Db));
            if (!admin || deviceGroupId != 0 || userGroupId != 0 || userId != 0)
            {
                string tmp = "INNER JOIN {1} ON {0}.ID = {1}.DeviceID ";
                tmp += "INNER JOIN {2} ON {1}.DeviceGroupID = {2}.ID ";
                tmp += "INNER JOIN {3} ON {2}.ID = {3}.DeviceGroupID ";
                tmp += "INNER JOIN {4} ON {3}.UserGroupID = {4}.ID ";
                tmp += "INNER JOIN {5} ON {4}.ID = {5}.UserGroupID ";                
                query += string.Format(tmp,
                    GuruxAMI.Server.AppHost.GetTableName<GXAmiDevice>(Db),
                    GuruxAMI.Server.AppHost.GetTableName<GXAmiDeviceGroupDevice>(Db),
                    GuruxAMI.Server.AppHost.GetTableName<GXAmiDeviceGroup>(Db),
                    GuruxAMI.Server.AppHost.GetTableName<GXAmiUserGroupDeviceGroup>(Db),
                    GuruxAMI.Server.AppHost.GetTableName<GXAmiUserGroup>(Db),
                    GuruxAMI.Server.AppHost.GetTableName<GXAmiUserGroupUser>(Db));
                if (!removed)
                {
                    Filter.Add(string.Format("{0}.Removed IS NULL AND {1}.Removed IS NULL AND {2}.Removed IS NULL AND {3}.Removed IS NULL",
                        GuruxAMI.Server.AppHost.GetTableName<GXAmiDevice>(Db),
                        GuruxAMI.Server.AppHost.GetTableName<GXAmiDeviceGroup>(Db),
                        GuruxAMI.Server.AppHost.GetTableName<GXAmiUserGroupDeviceGroup>(Db),
                        GuruxAMI.Server.AppHost.GetTableName<GXAmiUserGroup>(Db),
                        GuruxAMI.Server.AppHost.GetTableName<GXAmiUserGroupUser>(Db)));
                }
                if (userId != 0)
                {
                    Filter.Add("UserID = " + userId.ToString());
                }
                else if (!admin)
                {
                    Filter.Add("UserID = " + id.ToString());
                }
                if (userGroupId != 0)
                {
                    Filter.Add("UserGroupID = " + userGroupId.ToString());
                }
                if (deviceGroupId != 0)
                {
                    Filter.Add(GuruxAMI.Server.AppHost.GetTableName<GXAmiDeviceGroup>(Db) + ".ID = " + deviceGroupId.ToString());
                }
            }
            else if (!removed)
            {
                Filter.Add("Removed IS NULL");
            }
            if (deviceID != 0)
            {
                Filter.Add("DeviceID = " + deviceID.ToString());
            }
            if (Filter.Count != 0)
            {
                query += "WHERE ";
                query += string.Join(" AND ", Filter.ToArray());
            }
            return Db.Select<GXAmiDevice>(query);
        }

        /// <summary>
        /// Get available devices.
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
		public GXDevicesResponse Post(GXDevicesRequest request)
		{
            IAuthSession s = this.GetSession(false);
            List<GXAmiDevice> list;
            //Returns all devices of the user.
            if (request.UserID != 0)
            {
                list = GetDevices(s, Db, request.UserID, 0, 0, 0, request.Removed);
            }
            //Returns all devices from the user goup
            else if (request.UserGroupID != 0)
            {
                list = GetDevices(s, Db, 0, request.UserGroupID, 0, 0, request.Removed);
            }
            //Returns all devices from the device goup
            else if (request.DeviceGroupID != 0)
            {
                list = GetDevices(s, Db, 0, 0, request.DeviceGroupID, 0, request.Removed);
            }
            //Returns device
            else if (request.DeviceID != 0)
            {
                list = GetDevices(s, Db, 0, 0, 0, request.DeviceID, request.Removed);
            }
            //Returns all devices from the DC.
            else if (request.DataCollectorId != 0)
            {
                string query = string.Format("SELECT DISTINCT {0}.* FROM {0} INNER JOIN {1} ON {0}.ID = {1}.DeviceID WHERE DataCollectorID = {2} AND Removed IS NULL",
                GuruxAMI.Server.AppHost.GetTableName<GXAmiDevice>(Db),
                GuruxAMI.Server.AppHost.GetTableName<GXAmiDataCollectorDevice>(Db),
                request.DataCollectorId);
                list = Db.Select<GXAmiDevice>(query);
            }
            //Return all devices.
            else
            {
                list = GetDevices(s, Db, 0, 0, 0, 0, request.Removed);
            }
            foreach (GXAmiDevice it in list)
            {
                it.Parameters = Db.Select<GXAmiParameter>(q => q.ParentID == it.Id).ToArray();
                if (request.Content)
                {
                    it.Categories = Db.Select<GXAmiCategory>(q => q.DeviceID == it.Id).ToArray();
                    foreach (GXAmiCategory cat in it.Categories)
                    {
                        cat.Parameters = Db.Select<GXAmiParameter>(q => q.ParentID == cat.Id).ToArray();
                        cat.Properties = Db.Select<GXAmiProperty>(q => q.ParentID == cat.Id).ToArray();
                        foreach (GXAmiProperty p in cat.Properties)
                        {
                            p.Parameters = Db.Select<GXAmiParameter>(q => q.ParentID == p.Id).ToArray();
                        }
                    }
                    it.Tables = Db.Select<GXAmiDataTable>(q => q.DeviceID == it.Id).ToArray();
                    foreach (GXAmiDataTable table in it.Tables)
                    {
                        table.Parameters = Db.Select<GXAmiParameter>(q => q.ParentID == table.Id).ToArray();
                        table.Columns = Db.Select<GXAmiProperty>(q => q.ParentID == table.Id).ToArray();
                        foreach (GXAmiProperty p in table.Columns)
                        {
                            p.Parameters = Db.Select<GXAmiParameter>(q => q.ParentID == p.Id).ToArray();
                        }
                    }                    
                }
            }

            //Remove excluded devices.
            if (request.Excluded != null && request.Excluded.Length != 0)
            {
                List<ulong> ids = new List<ulong>(request.Excluded);
                var excludeUserGroups = from c in list where !ids.Contains(c.Id) select c;
                list = excludeUserGroups.ToList();
            }
            //Get devices by range.
            if (request.Index != 0 || request.Count != 0)
            {
                if (request.Count == 0 || request.Index + request.Count > list.Count)
                {
                    request.Count = list.Count - request.Index;
                }
                list.RemoveRange(0, request.Index);
                var limitUserGroups = list.Take(request.Count);
                list = limitUserGroups.ToList();
            }
            return new GXDevicesResponse(list.ToArray());
		}

        /// <summary>
        /// Can user access device.
        /// </summary>
        /// <param name="userId"></param>
        /// <param name="DeviceGroupId"></param>
        /// <returns></returns>
        static public bool CanUserAccessDevice(IDbConnection Db, long userId, ulong deviceId)
        {
            string query = "SELECT {0}.ID FROM {0}";
            query += "INNER JOIN {1} ON {0}.ID = {1}.DeviceID ";
            query += "INNER JOIN {2} ON {1}.DeviceGroupID = {2}.ID ";
            query += "INNER JOIN {3} ON {2}.ID = {3}.DeviceGroupID ";
            query += "INNER JOIN {4} ON {3}.UserGroupID = {4}.ID ";
            query += "INNER JOIN {5} ON {4}.ID = {5}.UserGroupID ";
            query = string.Format(query,
                GuruxAMI.Server.AppHost.GetTableName<GXAmiDevice>(Db),
                GuruxAMI.Server.AppHost.GetTableName<GXAmiDeviceGroupDevice>(Db),
                GuruxAMI.Server.AppHost.GetTableName<GXAmiDeviceGroup>(Db),
                GuruxAMI.Server.AppHost.GetTableName<GXAmiUserGroupDeviceGroup>(Db),
                GuruxAMI.Server.AppHost.GetTableName<GXAmiUserGroup>(Db),
                GuruxAMI.Server.AppHost.GetTableName<GXAmiUserGroupUser>(Db));
            query += string.Format("WHERE {0}.Removed IS NULL AND {1}.Removed IS NULL AND {2}.Removed IS NULL AND {3}.Removed IS NULL AND {4}.UserID = {5} AND {0}.ID = {6}",
                GuruxAMI.Server.AppHost.GetTableName<GXAmiDevice>(Db),
                GuruxAMI.Server.AppHost.GetTableName<GXAmiDeviceGroup>(Db),
                GuruxAMI.Server.AppHost.GetTableName<GXAmiUserGroupDeviceGroup>(Db),
                GuruxAMI.Server.AppHost.GetTableName<GXAmiUserGroup>(Db),
                GuruxAMI.Server.AppHost.GetTableName<GXAmiUserGroupUser>(Db),
                userId, deviceId);
            return Db.Select<GXAmiDeviceGroup>(query).Count != 0;
        }

		public GXDeviceDeleteResponse Post(GXDeviceDeleteRequest request)
		{
            IAuthSession s = this.GetSession(false);
            int id = Convert.ToInt32(s.Id);
            if (id == 0)
            {
                throw new ArgumentException("Remove failed. Invalid session ID.");
            }
            //Normal user can't remove device.
            if (!GuruxAMI.Server.GXBasicAuthProvider.CanUserEdit(s))
            {
                throw new ArgumentException("Access denied.");
            }
            bool superAdmin = GuruxAMI.Server.GXBasicAuthProvider.IsSuperAdmin(s);
            List<GXEventsItem> events = new List<GXEventsItem>();
            bool permanently = request.Permanently;
            List<GXAmiDevice> devices = new List<GXAmiDevice>();
            foreach (ulong it in request.DeviceIDs)
            {
                if (it == 0)
                {
                    throw new ArgumentException("ID is required");
                }
                if (!superAdmin && !CanUserAccessDevice(Db, id, it))
                {
                    throw new ArgumentException("Access denied.");
                }
                GXAmiDevice device = Db.QueryById<GXAmiDevice>(it);
                devices.Add(device);
                events.Add(new GXEventsItem(ActionTargets.Device, Actions.Remove, device));    
            }
            //Notify before delete or DC is not notified because device is not found from the DB.
            AppHost host = this.ResolveService<AppHost>();
            host.SetEvents(Db, this.Request, id, events);
            foreach (GXAmiDevice it in devices)
            {
                if (permanently)
                {
                    Db.DeleteById<GXAmiDevice>(it.Id);
                }
                else
                {
                    it.Removed = DateTime.Now;
                    Db.UpdateOnly(it, p => p.Removed, p => p.Id == it.Id);
                }
            }
			return new GXDeviceDeleteResponse();
		}
        
        /// <summary>
        /// Get values.
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        public GXValuesResponse Post(GXValuesRequest request)
        {
            List<GXAmiDataValue> list = new List<GXAmiDataValue>();
            foreach (ulong it in request.DeviceIDs)
            {
                if (request.LogValues)
                {
                    list.AddRange(Db.Select<GXAmiLatestValue>(q => q.DeviceID == it).ToArray());
                }
                else
                {
                    list.AddRange(Db.Select<GXAmiValueLog>(q => q.DeviceID == it).ToArray());
                }
            }
            return new GXValuesResponse(list.ToArray());
        }
       
        /// <summary>
        /// Change separated property values to the collection of rows.
        /// </summary>
        /// <returns></returns>
        object[][] ItemsToRows(GXAmiDataRow[] items, out ulong tableId, out uint startRow)
        {
            if (items == null || items.Length == 0)
            {
                startRow = 0;
                tableId = 0;
                return new object[0][];
            }
            uint maxcount = 0;
            startRow = items[0].RowIndex;
            tableId = items[0].TableID;
            uint rowIndex = items[0].RowIndex;
            foreach (GXAmiDataRow it in items)
            {

                if (it.ColumnIndex > maxcount)
                {
                    maxcount = it.ColumnIndex;
                }
                if (rowIndex != it.RowIndex)
                {
                    break;
                }
            }
            List<object[]> rows = new List<object[]>();
            object[] row = new object[maxcount + 1];
            //Make columns
            foreach (GXAmiDataRow it in items)
            {
                //Add new row when row index is changing.
                if (it.RowIndex != rowIndex)
                {
                    rows.Add(row);
                    row = new object[maxcount + 1];
                    rowIndex = it.RowIndex;
                }
                row[it.ColumnIndex] = it.UIValue;
            }
            rows.Add(row);
            return rows.ToArray();
        }

        /// <summary>
        /// Get table info.
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        public GXTableResponse Post(GXTableRequest request)
        {
            if (request.Type == TableRequestType.RowCount)
            {
                //Find last row number.
                SqlExpressionVisitor<GXAmiDataRow> ev = OrmLiteConfig.DialectProvider.ExpressionVisitor<GXAmiDataRow>();
                ev.Limit(1);
                ev.Where(q => q.TableID == request.TableId);
                ev.OrderByDescending(q => q.RowIndex);
                List<GXAmiDataRow> items = Db.Select<GXAmiDataRow>(ev);
                if (items.Count == 1)
                {
                    return new GXTableResponse(items[0].RowIndex);                    
                }
                //Table is empty.
                return new GXTableResponse(0);                                
            }
            else if (request.Type == TableRequestType.Rows)
            {
                //We must know columns count to retreave right amount of rows.
                int cnt = Db.Select<GXAmiProperty>(q => q.ParentID == request.TableId).Count;
                //Get rows
                SqlExpressionVisitor<GXAmiDataRow> ev = OrmLiteConfig.DialectProvider.ExpressionVisitor<GXAmiDataRow>();
                if (request.Count != 0)
                {
                    ev.Limit(cnt * request.Index, cnt * request.Count);
                }
                ev.OrderBy(q => q.RowIndex);                
                ev.Where(q => q.TableID == request.TableId);
                List<GXAmiDataRow> items = Db.Select<GXAmiDataRow>(ev);
                ulong tableId;
                uint startRow;
                return new GXTableResponse(ItemsToRows(items.ToArray(), out tableId, out startRow));
            }
            throw new ArgumentOutOfRangeException("Type");                        
        }         

        /// <summary>
        /// DC updates new value.
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        public GXValuesUpdateResponse Post(GXValuesUpdateRequest request)
        {
            if (request.Values != null && request.Values.Length != 0)
            {
                List<GXEventsItem> events = new List<GXEventsItem>();                                
                bool partOfTable = request.Values[0] is GXAmiDataRow;
                using (var trans = Db.OpenTransaction(IsolationLevel.ReadCommitted))
                {
                    if (partOfTable)
                    {
                        uint maxRow = 0;
                        //Find last row number.
                        SqlExpressionVisitor<GXAmiDataRow> ev = OrmLiteConfig.DialectProvider.ExpressionVisitor<GXAmiDataRow>();
                        ev.Limit(1);
                        ev.Where(q => q.TableID == (request.Values[0]as GXAmiDataRow).TableID);
                        ev.OrderByDescending(q => q.RowIndex);
                        List<GXAmiDataRow> items = Db.Select<GXAmiDataRow>(ev);
                        if (items.Count == 1)
                        {
                            maxRow = items[0].RowIndex;
                        }
                        else
                        {
                            maxRow = 0;
                        }
                        foreach (GXAmiDataRow it in request.Values)
                        {                            
                            //Increase row count.
                            if (it.RowIndex == 0 && it.ColumnIndex == 0)
                            {
                                ++maxRow;
                            }
                            it.TimeStamp = DateTime.Now;
                            if (it.RowIndex == 0)
                            {
                                it.RowIndex = maxRow;
                                Db.Insert(it);
                                it.Id = (ulong) Db.GetLastInsertId();
                            }
                            else
                            {
                                Db.Update(it);
                            }
                            events.Add(new GXEventsItem(ActionTargets.TableValueChanged, Actions.Add, it));
                        }                        
                    }
                    else
                    {
                        foreach (GXAmiDataValue it in request.Values)
                        {
                            //Delete old value from latest value.
                            Db.Delete<GXAmiLatestValue>(q => q.Id == it.PropertyID);
                            GXAmiLatestValue v = new GXAmiLatestValue();
                            v.TimeStamp = DateTime.Now;
                            v.PropertyID = it.PropertyID;
                            ulong mask = 0xFFFF;
                            v.DeviceID = v.PropertyID & ~mask;
                            v.UIValue = it.UIValue;
                            GXAmiValueLog lv = new GXAmiValueLog();
                            lv.PropertyID = it.PropertyID;
                            lv.DeviceID = v.DeviceID;
                            lv.UIValue = v.UIValue;
                            lv.TimeStamp = v.TimeStamp;
                            Db.Insert(v);
                            Db.Insert(lv);
                            events.Add(new GXEventsItem(ActionTargets.ValueChanged, Actions.Add, v));
                        }
                    }
                    trans.Commit();
                }
                AppHost host = this.ResolveService<AppHost>();
                host.SetEvents(Db, this.Request, 0, events);
            }
            return new GXValuesUpdateResponse();
        }

        /// <summary>
        /// Update new device state.
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        public GXDeviceStateUpdateResponse Put(GXDeviceStateUpdateRequest request)
        {
            List<GXEventsItem> events = new List<GXEventsItem>();
            foreach (var it in request.States)
            {
                GXAmiDevice device = Db.GetById<GXAmiDevice>(it.Key);
                device.State = it.Value;
                Db.UpdateOnly(device, p => p.StatesAsInt, p => p.Id == it.Key);
                events.Add(new GXEventsItem(ActionTargets.Device, Actions.State, device));
            }
            AppHost host = this.ResolveService<AppHost>();
            host.SetEvents(Db, this.Request, 0, events);
            return new GXDeviceStateUpdateResponse();
        }
	}
}
