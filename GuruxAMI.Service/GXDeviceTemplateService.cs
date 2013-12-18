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
using Gurux.Device;
using Gurux.Device.Editor;
using System.IO;
using System.Reflection;
using System.ComponentModel;
using System.Runtime.Serialization;
using GuruxAMI.Server;

namespace GuruxAMI.Service
{
    /// <summary>
    /// Service handles device tempate functionality.
    /// </summary>
	[Authenticate]
    internal class GXDeviceTemplateService : ServiceStack.ServiceInterface.Service
	{
        /// <summary>
        /// Load Device Template to own namespace.
        /// </summary>
        class GXProxyClass : MarshalByRefObject
        {
            void ImportParameters(object target, ulong id, object parent, List<object[]> items)
            {
                DataMemberAttribute dma = new DataMemberAttribute();
                object value;
                foreach (PropertyDescriptor it in TypeDescriptor.GetProperties(target))
                {
                    if (it.Name == "DisabledActions")
                    {
                        continue;
                    }
                    //If value is not stored.
                    DataMemberAttribute dm = it.Attributes[typeof(DataMemberAttribute)] as DataMemberAttribute;
                    if (dm == null)
                    {
                        continue;
                    }
                    ValueAccessAttribute va = it.Attributes[typeof(ValueAccessAttribute)] as ValueAccessAttribute;
                    if (va == null || va.RunTime != ValueAccessType.Edit)
                    {
                        continue;
                    }
                    //Save default value.
                    value = it.GetValue(target);
                    GXAmiParameterTemplate param = new GXAmiParameterTemplate();
                    param.ParentID = id;                    
                    param.Name = it.Name;
                    param.Value = value;
                    // param.Description
                    if (it.PropertyType == typeof(object) && param.Value != null)
                    {
                        param.Type = param.Value.GetType();
                    }
                    else
                    {
                        param.Type = it.PropertyType;
                    }
                    param.Storable = it.Attributes.Contains(dma);
                    param.Access = va.RunTime;
                    items.Add(new object[] { parent, param });
                }
            }

            string TargetDirectory;
            public List<object[]> Import(string path, string target, out byte[] data, out string assemblyName, out string addInType)
            {
                data = null;
                TargetDirectory = target;
                AppDomain.CurrentDomain.AssemblyResolve += new ResolveEventHandler(CurrentDomain_AssemblyResolve);
                GXDeviceType type = GXZip.Import(null, path, target);
                GXDeviceList.Update(target);
                string filename;
                if (type is Gurux.Device.PresetDevices.GXPublishedDeviceType)
                {
                    filename = Path.Combine(target, (type as Gurux.Device.PresetDevices.GXPublishedDeviceType).DeviceGuid + ".gxt");
                }
                else
                {
                    filename = Path.Combine(target, type.Name + ".gxt");
                }
                using (FileStream s = File.OpenRead(filename))
                {
                    long size = s.Length;
                    size = 0;
                }
                GXDevice device = GXDevice.Load(filename);
                addInType = device.AddIn.ToString();
                assemblyName = device.AddIn.GetType().Assembly.FullName;
                GXAmiDeviceTemplate dt = new GXAmiDeviceTemplate();
                dt.Guid = device.Guid;
                dt.Protocol = device.ProtocolName;
                dt.Template = device.DeviceType;
                dt.PresetName = device.PresetName;
                dt.Manufacturer = device.Manufacturer;
                dt.Model = device.Model;
                dt.Version = device.Version;
                dt.UpdateInterval = device.UpdateInterval;
                dt.WaitTime = device.WaitTime;
                dt.ResendCount = device.ResendCount;
                dt.Added = DateTime.Now.ToUniversalTime();
                List<GXAmiMediaType> list = new List<GXAmiMediaType>();
                List<string> medias = new List<string>(Gurux.Communication.GXClient.GetAvailableMedias());
                foreach (GXMediaType it in device.AllowedMediaTypes)
                {
                    GXAmiMediaType mt = new GXAmiMediaType();
                    mt.Name = it.Name;
                    //If default media settings are not given, ask them from the parser.
                    if (string.IsNullOrEmpty(it.DefaultMediaSettings) && medias.Contains(it.Name))
                    {
                        Gurux.Common.IGXMedia m = device.GXClient.SelectMedia(it.Name);                        
                        mt.Settings = m.Settings;
                    }
                    else
                    {
                        mt.Settings = it.DefaultMediaSettings;
                    }
                    list.Add(mt);
                }
                dt.AllowedMediaTypes = list.ToArray();
                List<object[]> items = new List<object[]>();
                items.Add(new object[]{dt, dt});
                ImportParameters(device, device.ID, dt, items);                
                foreach (GXCategory cat in device.Categories)
                {
                    GXAmiCategoryTemplate ct = new GXAmiCategoryTemplate();
                    items.Add(new object[]{dt, ct});
                    ct.Name = cat.Name;
                    ct.Id = cat.ID;
                    ImportParameters(cat, cat.ID, ct, items);
                    foreach (GXProperty prop in cat.Properties)
                    {
                        GXAmiPropertyTemplate pt = new GXAmiPropertyTemplate();
                        pt.Id = prop.ID;
                        pt.ParentID = cat.ID;
                        pt.Name = prop.Name;
                        pt.Unit = prop.Unit;
                        if (prop.ValueType != null)
                        {
                            pt.TypeAsString = prop.ValueType.ToString();
                        }
                        pt.AccessMode = prop.AccessMode;
                        items.Add(new object[]{dt, pt});
                        ImportParameters(prop, prop.ID, pt, items);
                    }                    
                }

                foreach (GXTable table in device.Tables)
                {
                    GXAmiTableTemplate tt = new GXAmiTableTemplate();
                    items.Add(new object[] { dt, tt });
                    tt.Name = table.Name;
                    tt.Id = table.ID;
                    ImportParameters(table, table.ID, tt, items);
                    foreach (GXProperty prop in table.Columns)
                    {
                        GXAmiPropertyTemplate pt = new GXAmiPropertyTemplate();
                        pt.Id = prop.ID;
                        pt.ParentID = table.ID;
                        pt.Name = prop.Name;
                        pt.Unit = prop.Unit;
                        if (prop.ValueType != null)
                        {
                            pt.TypeAsString = prop.ValueType.ToString();
                        }
                        pt.AccessMode = prop.AccessMode;
                        items.Add(new object[] { dt, pt });
                        ImportParameters(prop, prop.ID, pt, items);
                    }
                }
                return items;
            }

            System.Reflection.Assembly CurrentDomain_AssemblyResolve(object sender, ResolveEventArgs args)
            {
                foreach (string it in Directory.GetFiles(TargetDirectory, "*.dll"))
                {
                    Assembly asm = Assembly.LoadFile(it);
                    if (asm.GetName().ToString() == args.Name)
                    {
                        return asm;
                    }
                }
                return null;
            }
        }

        GXAmiDeviceTemplateDataBlock[] SplitDataToPackets(ulong templateId, string path)
        {
            List<GXAmiDeviceTemplateDataBlock> packets = new List<GXAmiDeviceTemplateDataBlock>();
            byte[] buff = new byte[65535];
            int len;
            int index = -1;
            using (FileStream f = File.OpenRead(path))
            {
                while ((len = f.Read(buff, 0, 65535)) != 0)
                {
                    ++index;
                    packets.Add(new GXAmiDeviceTemplateDataBlock(templateId, index, buff, len));
                }
            }
            return packets.ToArray();
        }

        byte[] JoinPackets(IDbConnection Db, ulong templateId)
        {
            List<GXAmiDeviceTemplateDataBlock> list = Db.Select<GXAmiDeviceTemplateDataBlock>(q => q.DeviceTemplateId == templateId);
            List<byte> data = new List<byte>();
            foreach (GXAmiDeviceTemplateDataBlock it in list)
            {
                data.AddRange(it.Data);
            }
            return data.ToArray();
        }

        /// <summary>
        /// Add new device template.
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
		public GXDeviceTemplateUpdateResponse Post(GXDeviceTemplateUpdateRequest request)
		{
            List<GXEventsItem> events = new List<GXEventsItem>();
            GXAmiDeviceTemplate deviceTemplate = null;
            using (var trans = Db.OpenTransaction(IsolationLevel.ReadCommitted))
            {
                string filename = Path.GetTempFileName();
                using (FileStream stream = new FileStream(filename, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    BinaryWriter w = new BinaryWriter(stream);
                    w.Write(request.Data);
                }
                string pathToDll = this.GetType().Assembly.CodeBase;
                // Create an Application Domain:
                AppDomainSetup domainSetup = new AppDomainSetup { PrivateBinPath = pathToDll };
                System.AppDomain td = null;
                string dir = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString() + Path.DirectorySeparatorChar);
                //Try to load device template and unload assmbly.                          
                try
                {
                    td = AppDomain.CreateDomain("TestDomain", null, domainSetup);
                    GXProxyClass pc = (GXProxyClass)(td.CreateInstanceFromAndUnwrap(pathToDll, typeof(GXProxyClass).FullName));
                    if (!Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }
                    byte[] data;
                    string addInType, assemblyName;                    
                    List<object[]> items = pc.Import(filename, dir, out data, out assemblyName, out addInType);
                    ulong DeviceTemplateID = 0;
                    ulong categoryTemplateID = 0;
                    ulong propertyTemplateID = 0;
                    ulong tableTemplateID = 0;
                    int version = 0;
                    foreach (object[] it in items)
                    {                        
                        if (it[1] is GXAmiDeviceTemplate)
                        {
                            deviceTemplate = it[1] as GXAmiDeviceTemplate;
                            events.Add(new GXEventsItem(ActionTargets.DeviceTemplate, Actions.Add, deviceTemplate));    
                            //Find new version number for the template.
                            foreach (GXAmiDeviceTemplate dt in Db.Select<GXAmiDeviceTemplate>(q => q.Guid == deviceTemplate.Guid))
                            {
                                if (dt.TemplateVersion > version)
                                {
                                    version = dt.TemplateVersion;
                                }
                            }
                            ++version;
                            deviceTemplate.ProtocolAddInType = addInType;
                            deviceTemplate.ProtocolAssembly = assemblyName;
                            deviceTemplate.TemplateVersion = version;
                        } 
                        else if (it[1] is GXAmiCategoryTemplate)
                        {
                            tableTemplateID = 0;
                            (it[1] as GXAmiCategoryTemplate).DeviceID = DeviceTemplateID;
                            (it[1] as GXAmiCategoryTemplate).Id += DeviceTemplateID << 16;
                            (it[1] as GXAmiCategoryTemplate).TemplateVersion = version;
                        }
                        else if (it[1] is GXAmiTableTemplate)
                        {
                            categoryTemplateID = 0;
                            (it[1] as GXAmiTableTemplate).DeviceID = DeviceTemplateID;
                            (it[1] as GXAmiTableTemplate).Id += DeviceTemplateID << 16;
                            (it[1] as GXAmiTableTemplate).TemplateVersion = version;
                        }
                        else if (it[1] is GXAmiPropertyTemplate)
                        {
                            (it[1] as GXAmiPropertyTemplate).DeviceID = DeviceTemplateID;
                            (it[1] as GXAmiPropertyTemplate).TemplateVersion = version;
                            if (categoryTemplateID != 0)
                            {
                                (it[1] as GXAmiPropertyTemplate).ParentID = categoryTemplateID;
                            }
                            else if (tableTemplateID != 0)
                            {
                                (it[1] as GXAmiPropertyTemplate).ParentID = tableTemplateID;
                            }
                            else
                            {
                                throw new ArgumentOutOfRangeException("Parent ID.");
                            }
                            (it[1] as GXAmiPropertyTemplate).Id += DeviceTemplateID << 16;
                        }
                        else if (it[1] is GXAmiParameterTemplate)
                        {
                            (it[1] as GXAmiParameterTemplate).DeviceID = DeviceTemplateID;
                            (it[1] as GXAmiParameterTemplate).TemplateVersion = version;
                            if (it[0] is GXAmiDeviceTemplate)
                            {
                                (it[1] as GXAmiParameterTemplate).ParentID = DeviceTemplateID << 16;
                            }
                            else if (it[0] is GXAmiCategoryTemplate)
                            {
                                (it[1] as GXAmiParameterTemplate).ParentID = categoryTemplateID;
                            }
                            else if (it[0] is GXAmiTableTemplate)
                            {
                                (it[1] as GXAmiParameterTemplate).ParentID = tableTemplateID;
                            }
                            else if (it[0] is GXAmiPropertyTemplate)
                            {
                                (it[1] as GXAmiParameterTemplate).ParentID = propertyTemplateID;
                            }
                        }
                        Db.Insert(it[1]);                        
                        if (it[1] is GXAmiDeviceTemplate)
                        {
                            ulong value = (ulong)Db.GetLastInsertId();
                            (it[1] as GXAmiDeviceTemplate).Id = value;
                            DeviceTemplateID = value;
                            //Update allowed media types.
                            foreach (GXAmiMediaType mt in (it[1] as GXAmiDeviceTemplate).AllowedMediaTypes)
                            {
                                mt.DeviceTemplateId = DeviceTemplateID;
                                Db.Insert(mt);
                            }
                        }
                        else if (it[1] is GXAmiCategoryTemplate)
                        {
                            categoryTemplateID = (it[1] as GXAmiCategoryTemplate).Id;
                        }
                        else if (it[1] is GXAmiTableTemplate)
                        {
                            tableTemplateID = (it[1] as GXAmiTableTemplate).Id;
                        }
                        else if (it[1] is GXAmiPropertyTemplate)
                        {
                            propertyTemplateID = (it[1] as GXAmiPropertyTemplate).Id;
                        }
                    }
                    //Save device template to data blocks.
                    foreach(GXAmiDeviceTemplateDataBlock it in SplitDataToPackets(DeviceTemplateID, filename))
                    {
                        Db.Insert(it);
                    }
                    foreach (GXAmiDeviceTemplateDataBlock it in SplitDataToPackets(DeviceTemplateID, filename))
                    {
                        Db.Insert(it);
                    }
                    if (request.UserGroups != null)
                    {
                        foreach (long ugId in request.UserGroups)
                        {
                            GXAmiUserGroupDeviceTemplate it = new GXAmiUserGroupDeviceTemplate();
                            it.DeviceTemplateID = DeviceTemplateID;
                            it.UserGroupID = ugId;
                            Db.Insert(it);
                        }
                    }
                }
                finally
                {
                    // Unload the application domain:
                    if (td != null)
                    {
                        System.AppDomain.Unload(td);
                    }
                    if (dir != null)
                    {
                        try
                        {
                            Directory.Delete(dir, true);
                        }
                        catch
                        {
                            //It's OK if this fails.
                        }
                    }
                }
                trans.Commit();
            }
            AppHost host = this.ResolveService<AppHost>();
            host.SetEvents(Db, this.Request, 0, events);
            return new GXDeviceTemplateUpdateResponse(request.UserGroups, deviceTemplate);
		}

        List<GXAmiDeviceTemplate> GetDeviceTemplates(IAuthSession s, IDbConnection Db, long userId, long userGroupId, bool? preset, string protocol, bool removed)
        {
            string id = s.Id;
            bool admin = GuruxAMI.Server.GXBasicAuthProvider.IsSuperAdmin(s);
            List<string> Filter = new List<string>();
            string query = "SELECT * FROM " + GuruxAMI.Server.AppHost.GetTableName<GXAmiDeviceTemplate>(Db) + " ";
            if (!admin || userGroupId != 0 || userId != 0)
            {
                string tmp = "INNER JOIN {1} ON {0}.ID = {1}.DeviceTemplateID ";
                tmp += "INNER JOIN {2} ON {1}.UserGroupID = {2}.ID ";
                tmp += "INNER JOIN {3} ON {2}.ID = {3}.UserGroupID ";                
                query += string.Format(tmp,
                    GuruxAMI.Server.AppHost.GetTableName<GXAmiDeviceTemplate>(Db),
                    GuruxAMI.Server.AppHost.GetTableName<GXAmiUserGroupDeviceTemplate>(Db),
                    GuruxAMI.Server.AppHost.GetTableName<GXAmiUserGroup>(Db),
                    GuruxAMI.Server.AppHost.GetTableName<GXAmiUserGroupUser>(Db));

                if (!removed)
                {
                    Filter.Add(string.Format("{0}.Removed IS NULL",
                        GuruxAMI.Server.AppHost.GetTableName<GXAmiDeviceTemplate>(Db)));
                }
                Filter.Add(string.Format("{0}.Removed IS NULL AND {1}.Removed IS NULL",                        
                        GuruxAMI.Server.AppHost.GetTableName<GXAmiUserGroup>(Db),
                        GuruxAMI.Server.AppHost.GetTableName<GXAmiUserGroupUser>(Db)));
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
            }
            else if (!removed)
            {
                Filter.Add("Removed IS NULL");
            }

            if (preset != null)
            {
                bool value = preset.Value;
                if (value)
                {
                    Filter.Add("PresetName IS NOT NULL");
                }
                else
                {
                    Filter.Add("PresetName IS NULL");
                }
            }
            if (!string.IsNullOrEmpty(protocol))
            {
                Filter.Add("Protocol = " + protocol);
            }      
            if (Filter.Count != 0)
            {
                query += "WHERE ";
                query += string.Join(" AND ", Filter.ToArray());
            }
            return Db.Select<GXAmiDeviceTemplate>(query);
        }

        /// <summary>
        /// Return all device templates that user can access.
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
		public GXDeviceTemplatesResponse Post(GXDeviceTemplatesRequest request)
		{
            IAuthSession s = this.GetSession(false);
            List<GXAmiDeviceTemplate> list;
            //Returns all devices templates of the user.
            if (request.UserID != 0)
            {
                list = GetDeviceTemplates(s, Db, request.UserID, 0, request.Preset, request.Protocol, request.Removed);
            }
            //Returns all devices templates from the user goup
            else if (request.UserGroupID != 0)
            {
                list = GetDeviceTemplates(s, Db, 0, request.UserGroupID, request.Preset, request.Protocol, request.Removed);
            }
            //Returns all devices templates that gives device(s) use.
            else if (request.DeviceIDs != null)
            {
                list = new List<GXAmiDeviceTemplate>();
                foreach (ulong it in request.DeviceIDs)
                {
                    string query = string.Format("SELECT {0}.* FROM {0} INNER JOIN {1} ON {0}.ID = {1}.TemplateId WHERE {1}.Removed IS NULL AND {1}.ID = {2}",
                        GuruxAMI.Server.AppHost.GetTableName<GXAmiDeviceTemplate>(Db),
                        GuruxAMI.Server.AppHost.GetTableName<GXAmiDevice>(Db),
                        it);
                    list.AddRange(Db.Select<GXAmiDeviceTemplate>(query));
                }
            }
            //Returns all devices templates that gives data collectors use.
            else if (request.DataCollectorIDs != null)
            {
                list = new List<GXAmiDeviceTemplate>();
                foreach (ulong it in request.DataCollectorIDs)
                {
                    string query = string.Format("SELECT {0}.* FROM {0} INNER JOIN {1} ON {0}.ID = {1}.TemplateId INNER JOIN {2} ON {1}.ID = {2}.DeviceID INNER JOIN {3} ON {2}.DataCollectorID = {3}.ID WHERE {1}.Removed IS NULL AND {3}.ID = {4}", 
                        GuruxAMI.Server.AppHost.GetTableName<GXAmiDeviceTemplate>(Db),
                        GuruxAMI.Server.AppHost.GetTableName<GXAmiDevice>(Db),
                        GuruxAMI.Server.AppHost.GetTableName<GXAmiDataCollectorDevice>(Db),
                        GuruxAMI.Server.AppHost.GetTableName<GXAmiDataCollector>(Db),                        
                        it);
                    list.AddRange(Db.Select<GXAmiDeviceTemplate>(query));
                }
            }
            //Returns all device templates            
            else
            {
                list = GetDeviceTemplates(s, Db, 0, 0, request.Preset, request.Protocol, request.Removed);
            }
            //Get allowed mediatypes.
            foreach (GXAmiDeviceTemplate it in list)
            {
                it.AllowedMediaTypes = Db.Select<GXAmiMediaType>(q => q.DeviceTemplateId == it.Id).ToArray();
            }
            return new GXDeviceTemplatesResponse(list.ToArray());
		}

        public GXDeviceTemplateDataResponse Post(GXDeviceTemplateDataRequest request)
        {
            byte[] data = null;
            if (request.DeviceId != 0)
            {
                List<GXAmiDevice> devices = Db.Select<GXAmiDevice>(q => q.Id == request.DeviceId);
                if (devices.Count != 1)
                {
                    throw new ArgumentException("Access denied.");
                }
                data = JoinPackets(Db, devices[0].TemplateId);
            }
            else if (request.DeviceTemplateGuid != Guid.Empty)
            {
                List<GXAmiDeviceTemplate> templates = Db.Select<GXAmiDeviceTemplate>(q => q.Guid == request.DeviceTemplateGuid);
                if (templates.Count != 1)
                {
                    throw new ArgumentException("Access denied.");
                }
                data = JoinPackets(Db, templates[0].Id);
            }
            else
            {
                throw new ArgumentException("Access denied.");
            }
            return new GXDeviceTemplateDataResponse(data);
        }
        

        /// <summary>
        /// Create new device from the device template.
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        public GXCreateDeviceResponse Get(GXCreateDeviceRequest request)
        {
            //Create devices from the device templates.
            List<GXAmiDevice> devices = new List<GXAmiDevice>();
            foreach (ulong id in request.Ids)
            {
                List<GXAmiDeviceTemplate> tmp = Db.Select<GXAmiDeviceTemplate>(q => q.Id == id);
                if (tmp.Count != 1)
                {
                    throw new ArgumentException("Access denied.");
                }
                GXAmiDevice dev = tmp[0] as GXAmiDevice;
                dev.Guid = Guid.NewGuid();
                dev.Id = 0;
                dev.TemplateId = id;
                dev.Parameters = Db.Select<GXAmiParameterTemplate>(q => q.ParentID == id << 16).ToArray();
                //Reset parameter IDs.
                foreach (GXAmiParameter p in dev.Parameters)
                {
                    p.Id = 0;
                }
                devices.Add(dev);
            }          
            return new GXCreateDeviceResponse(devices.ToArray());
        }

        /// <summary>
        /// Return selected devices.
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        public GXDeviceGetResponse Get(GXDeviceGetRequest request)
        {
            List<GXAmiDevice> devices = new List<GXAmiDevice>();
            foreach (ulong id in request.Ids)
            {
                string query = string.Format("SELECT {0}.* FROM {0} WHERE {0}.ID = {1}",
                    GuruxAMI.Server.AppHost.GetTableName<GXAmiDevice>(Db),
                    id);
                List<GXAmiDevice> tmp = Db.Select<GXAmiDevice>(query);
                if (tmp.Count != 1)
                {
                    throw new ArgumentException("Access denied.");
                }                
                GXAmiDevice device = tmp[0];
                devices.Add(device);
                device.Parameters = Db.Select<GXAmiParameter>(q => q.ParentID == device.Id).ToArray();
                device.Categories = Db.Select<GXAmiCategory>(q => q.DeviceID == device.Id).ToArray();
                foreach (GXAmiCategory cat in device.Categories)
                {
                    cat.Parameters = Db.Select<GXAmiParameter>(q => q.ParentID == cat.Id).ToArray();
                    cat.Properties = Db.Select<GXAmiProperty>(q => q.ParentID == cat.Id).ToArray();
                    foreach (GXAmiProperty p in cat.Properties)
                    {
                        p.Parameters = Db.Select<GXAmiParameter>(q => q.ParentID == p.Id).ToArray();
                    }
                }
            }
            return new GXDeviceGetResponse(devices.ToArray());
        }         

        /// <summary>
        /// Remove selected device template
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
		public GXDeviceTemplateDeleteResponse Post(GXDeviceTemplateDeleteRequest request)
		{
            List<GXEventsItem> events = new List<GXEventsItem>();
            IAuthSession s = this.GetSession(false);
            int id = Convert.ToInt32(s.Id);
            if (id == 0)
            {
                throw new ArgumentException("Remove failed. Invalid session ID.");
            }
            bool superAdmin = GuruxAMI.Server.GXBasicAuthProvider.IsSuperAdmin(s);
            foreach (ulong it in request.DeviceTemplateIDs)
            {
                if (it == 0)
                {
                    throw new ArgumentException("ID is required");
                }
                if (!superAdmin && GetDeviceTemplates(s, Db, id, 0, false, null, false).Count == 0)
                {
                    throw new ArgumentException("Access denied.");
                }
                GXAmiDeviceTemplate dt = Db.GetById<GXAmiDeviceTemplate>(it);
                if (request.Permanently)
                {
                    Db.DeleteById<GXAmiDeviceTemplate>(it);
                }
                else
                {
                    Db.UpdateOnly(new GXAmiDeviceTemplate { Removed = DateTime.Now }, p => p.Removed, p => p.Id == it);
                }
                events.Add(new GXEventsItem(ActionTargets.DeviceTemplate, Actions.Remove, dt));    
            }
            AppHost host = this.ResolveService<AppHost>();
            host.SetEvents(Db, this.Request, 0, events);
			return new GXDeviceTemplateDeleteResponse();
		}
	}
}
