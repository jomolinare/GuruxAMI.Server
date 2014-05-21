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
using Gurux.Device;
using Gurux.Device.Editor;
using System.IO;
using System.Reflection;
using System.ComponentModel;
using System.Runtime.Serialization;
using GuruxAMI.Server;
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
    /// Service handles device tempate functionality.
    /// </summary>
	[Authenticate]
    internal class GXDeviceProfileservice : GXService
	{
        /// <summary>
        /// Load Device Template to own namespace.
        /// </summary>
        class GXProxyClass : MarshalByRefObject
        {
            /// <summary>
            /// Inport parameters to the DB.
            /// </summary>
            /// <param name="target">Device, Category, Table or property that parameters are saved.</param>
            /// <param name="id">Target ID.</param>
            /// <param name="parent">Parent template.</param>
            /// <param name="items">Keyvalue pair template who owns the parameter and value of the parameter.</param>
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
                    //If parameter is enum add possible values to the DB.
                    if (it.PropertyType.IsEnum)
                    {
                        List<GXAmiValueItem> values = new List<GXAmiValueItem>();
                        foreach(string val in Enum.GetNames(it.PropertyType))
                        {
                            GXAmiValueItem vi = new GXAmiValueItem();
                            //Parameter ID is updated after parameter template is saved to the DB.
                            vi.ParameterId = 0;
                            vi.UIValue = val;
                            values.Add(vi);
                        }
                        param.Values = values.ToArray();
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
                GXDeviceProfile type = GXZip.Import(null, path, target);
                GXDeviceList.Update(target);
                string filename = Path.Combine(target, type.DeviceGuid + ".gxp");
                using (FileStream s = File.OpenRead(filename))
                {
                    long size = s.Length;
                    size = 0;
                }
                GXDevice device = GXDevice.Load(filename);
                addInType = device.AddIn.ToString();
                assemblyName = device.AddIn.GetType().Assembly.FullName;
                GXAmiDeviceProfile dt = new GXAmiDeviceProfile();
                dt.Guid = device.Guid;
                dt.Protocol = device.ProtocolName;
                dt.Profile = device.DeviceProfile;
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
                    GXAmiDataTableTemplate tt = new GXAmiDataTableTemplate();
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

        GXAmiDeviceProfilesDataBlock[] SplitDataToPackets(ulong templateId, string path)
        {
            List<GXAmiDeviceProfilesDataBlock> packets = new List<GXAmiDeviceProfilesDataBlock>();
            byte[] buff = new byte[65535];
            int len;
            int index = -1;
            using (FileStream f = File.OpenRead(path))
            {
                while ((len = f.Read(buff, 0, 65535)) != 0)
                {
                    ++index;
                    packets.Add(new GXAmiDeviceProfilesDataBlock(templateId, index, buff, len));
                }
            }
            return packets.ToArray();
        }

        byte[] JoinPackets(IDbConnection Db, ulong templateId)
        {
            List<GXAmiDeviceProfilesDataBlock> list = Db.Select<GXAmiDeviceProfilesDataBlock>(q => q.DeviceProfilesId == templateId);
            List<byte> data = new List<byte>();
            foreach (GXAmiDeviceProfilesDataBlock it in list)
            {
                data.AddRange(it.Data);
            }
            return data.ToArray();
        }

        /// <summary>
        /// Add new device profile.
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
		public GXDeviceProfilesUpdateResponse Post(GXDeviceProfilesUpdateRequest request)
		{
            List<GXEventsItem> events = new List<GXEventsItem>();
            GXAmiDeviceProfile DeviceProfiles = null;
            lock (Db)
            {
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
                        ulong DeviceProfileID = 0;
                        ulong categoryTemplateID = 0;
                        ulong propertyTemplateID = 0;
                        ulong tableTemplateID = 0;
                        int version = 0;
                        foreach (object[] it in items)
                        {
                            if (it[1] is GXAmiDeviceProfile)
                            {
                                DeviceProfiles = it[1] as GXAmiDeviceProfile;
                                events.Add(new GXEventsItem(ActionTargets.DeviceProfile, Actions.Add, DeviceProfiles));
                                //Find new version number for the template.
                                foreach (GXAmiDeviceProfile dt in Db.Select<GXAmiDeviceProfile>(q => q.Guid == DeviceProfiles.Guid))
                                {
                                    if (dt.ProfileVersion > version)
                                    {
                                        version = dt.ProfileVersion;
                                    }
                                }
                                ++version;
                                DeviceProfiles.ProtocolAddInType = addInType;
                                DeviceProfiles.ProtocolAssembly = assemblyName;
                                DeviceProfiles.ProfileVersion = version;
                            }
                            else if (it[1] is GXAmiCategoryTemplate)
                            {
                                tableTemplateID = 0;
                                (it[1] as GXAmiCategoryTemplate).DeviceID = DeviceProfileID;
                                (it[1] as GXAmiCategoryTemplate).Id += DeviceProfileID << 16;
                                (it[1] as GXAmiCategoryTemplate).TemplateVersion = version;
                            }
                            else if (it[1] is GXAmiDataTableTemplate)
                            {
                                categoryTemplateID = 0;
                                (it[1] as GXAmiDataTableTemplate).DeviceID = DeviceProfileID;
                                (it[1] as GXAmiDataTableTemplate).Id += DeviceProfileID << 16;
                                (it[1] as GXAmiDataTableTemplate).TemplateVersion = version;
                            }
                            else if (it[1] is GXAmiPropertyTemplate)
                            {
                                (it[1] as GXAmiPropertyTemplate).DeviceID = DeviceProfileID;
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
                                (it[1] as GXAmiPropertyTemplate).Id += DeviceProfileID << 16;
                            }
                            else if (it[1] is GXAmiParameterTemplate)
                            {
                                (it[1] as GXAmiParameterTemplate).DeviceID = DeviceProfileID;
                                (it[1] as GXAmiParameterTemplate).TemplateVersion = version;
                                if (it[0] is GXAmiDeviceProfile)
                                {
                                    (it[1] as GXAmiParameterTemplate).ParentID = DeviceProfileID << 16;
                                }
                                else if (it[0] is GXAmiCategoryTemplate)
                                {
                                    (it[1] as GXAmiParameterTemplate).ParentID = categoryTemplateID;
                                }
                                else if (it[0] is GXAmiDataTableTemplate)
                                {
                                    (it[1] as GXAmiParameterTemplate).ParentID = tableTemplateID;
                                }
                                else if (it[0] is GXAmiPropertyTemplate)
                                {
                                    (it[1] as GXAmiParameterTemplate).ParentID = propertyTemplateID;
                                }                                
                            }
                            Db.Insert(it[1]);
                            if (it[1] is GXAmiDeviceProfile)
                            {
#if !SS4
                                ulong value = (ulong)Db.GetLastInsertId();
#else
                                ulong value = (ulong)Db.LastInsertId();
#endif                                                                   
                                (it[1] as GXAmiDeviceProfile).Id = value;
                                DeviceProfileID = value;
                                //Update allowed media types.
                                foreach (GXAmiMediaType mt in (it[1] as GXAmiDeviceProfile).AllowedMediaTypes)
                                {
                                    mt.DeviceProfileId = DeviceProfileID;
                                    Db.Insert(mt);
                                }
                            }
                            else if (it[1] is GXAmiCategoryTemplate)
                            {
                                categoryTemplateID = (it[1] as GXAmiCategoryTemplate).Id;
                            }
                            else if (it[1] is GXAmiDataTableTemplate)
                            {
                                tableTemplateID = (it[1] as GXAmiDataTableTemplate).Id;
                            }
                            else if (it[1] is GXAmiPropertyTemplate)
                            {
                                propertyTemplateID = (it[1] as GXAmiPropertyTemplate).Id;
                                if ((it[1] as GXAmiPropertyTemplate).Values != null && (it[1] as GXAmiPropertyTemplate).Values.Length != 0)
                                {
#if !SS4
                                    ulong value = (ulong)Db.GetLastInsertId();
#else
                                    ulong value = (ulong)Db.LastInsertId();
#endif
                                    foreach (GXAmiValueItem vi in (it[1] as GXAmiPropertyTemplate).Values)
                                    {
                                        vi.ProfileId = DeviceProfileID;
                                        vi.PropertyId = value;
                                        Db.Insert(vi);
                                    }
                                }
                            }
                            else if (it[1] is GXAmiParameterTemplate)
                            {
                                if ((it[1] as GXAmiParameterTemplate).Values != null && (it[1] as GXAmiParameterTemplate).Values.Length != 0)
                                {
#if !SS4
                                    ulong value = (ulong)Db.GetLastInsertId();
#else
                                ulong value = (ulong)Db.LastInsertId();
#endif
                                    foreach (GXAmiValueItem vi in (it[1] as GXAmiParameterTemplate).Values)
                                    {
                                        vi.ProfileId = DeviceProfileID;
                                        vi.ParameterId = value;
                                        Db.Insert(vi);
                                    }
                                }
                            }
                        }
                        //Save device template to data blocks.
                        foreach (GXAmiDeviceProfilesDataBlock it in SplitDataToPackets(DeviceProfileID, filename))
                        {
                            Db.Insert(it);
                        }
                        foreach (GXAmiDeviceProfilesDataBlock it in SplitDataToPackets(DeviceProfileID, filename))
                        {
                            Db.Insert(it);
                        }
                        if (request.UserGroups != null)
                        {
                            foreach (long ugId in request.UserGroups)
                            {
                                GXAmiUserGroupDeviceProfile it = new GXAmiUserGroupDeviceProfile();
                                it.DeviceProfileID = DeviceProfileID;
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
            }
            AppHost host = this.ResolveService<AppHost>();
            host.SetEvents(Db, this.Request, 0, events);
            return new GXDeviceProfilesUpdateResponse(request.UserGroups, DeviceProfiles);
        }

        List<GXAmiDeviceProfile> GetDeviceProfiles(IAuthSession s, IDbConnection Db, long userId, long userGroupId, bool? preset, string protocol, bool removed)
        {
            lock (Db)
            {
                string id = s.Id;
                bool admin = GuruxAMI.Server.GXBasicAuthProvider.IsSuperAdmin(s);
                List<string> Filter = new List<string>();
                string query = "SELECT * FROM " + GuruxAMI.Server.AppHost.GetTableName<GXAmiDeviceProfile>(Db) + " ";
                if (!admin || userGroupId != 0 || userId != 0)
                {
                    string tmp = "INNER JOIN {1} ON {0}.ID = {1}.DeviceProfilesID ";
                    tmp += "INNER JOIN {2} ON {1}.UserGroupID = {2}.ID ";
                    tmp += "INNER JOIN {3} ON {2}.ID = {3}.UserGroupID ";
                    query += string.Format(tmp,
                        GuruxAMI.Server.AppHost.GetTableName<GXAmiDeviceProfile>(Db),
                        GuruxAMI.Server.AppHost.GetTableName<GXAmiUserGroupDeviceProfile>(Db),
                        GuruxAMI.Server.AppHost.GetTableName<GXAmiUserGroup>(Db),
                        GuruxAMI.Server.AppHost.GetTableName<GXAmiUserGroupUser>(Db));

                    if (!removed)
                    {
                        Filter.Add(string.Format("{0}.Removed IS NULL",
                            GuruxAMI.Server.AppHost.GetTableName<GXAmiDeviceProfile>(Db)));
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
                return Db.Select<GXAmiDeviceProfile>(query);
            }
        }

        /// <summary>
        /// Return all device profiles that user can access.
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
		public GXDeviceProfilesResponse Post(GXDeviceProfilesRequest request)
		{
            lock (Db)
            {
                IAuthSession s = this.GetSession(false);
                List<GXAmiDeviceProfile> list;
                //Returns all devices profiles of the user.
                if (request.UserID != 0)
                {
                    list = GetDeviceProfiles(s, Db, request.UserID, 0, request.Preset, request.Protocol, request.Removed);
                }
                //Returns all devices profiles from the user goup
                else if (request.UserGroupID != 0)
                {
                    list = GetDeviceProfiles(s, Db, 0, request.UserGroupID, request.Preset, request.Protocol, request.Removed);
                }
                //Returns all devices profiles that gives device(s) use.
                else if (request.DeviceIDs != null && request.DeviceIDs.Length != 0)
                {
                    list = new List<GXAmiDeviceProfile>();
                    foreach (ulong it in request.DeviceIDs)
                    {
                        string query = string.Format("SELECT {0}.* FROM {0} INNER JOIN {1} ON {0}.ID = {1}.ProfileId WHERE {1}.Removed IS NULL AND {1}.ID = {2}",
                            GuruxAMI.Server.AppHost.GetTableName<GXAmiDeviceProfile>(Db),
                            GuruxAMI.Server.AppHost.GetTableName<GXAmiDevice>(Db),
                            it);
                        list.AddRange(Db.Select<GXAmiDeviceProfile>(query));
                    }
                }
                //Returns all devices profiles.
                else if (request.ProfileIDs != null && request.ProfileIDs.Length != 0)
                {
                    list = new List<GXAmiDeviceProfile>();
                    foreach (ulong it in request.ProfileIDs)
                    {
                        string query = string.Format("SELECT {0}.* FROM {0} WHERE {0}.ID = {1}",
                            GuruxAMI.Server.AppHost.GetTableName<GXAmiDeviceProfile>(Db),
                            it);
                        list.AddRange(Db.Select<GXAmiDeviceProfile>(query));
                    }
                }
                //Returns all devices profiles that gives data collectors use.
                else if (request.DataCollectorIDs != null && request.DataCollectorIDs.Length != 0)
                {
                    list = new List<GXAmiDeviceProfile>();
                    foreach (ulong it in request.DataCollectorIDs)
                    {
                        string query = string.Format("SELECT {0}.* FROM {0} INNER JOIN {1} ON {0}.ID = {1}.ProfileId INNER JOIN {2} ON {1}.ID = {2}.DeviceID INNER JOIN {3} ON ({2}.DataCollectorID IS NULL OR {2}.DataCollectorID = {3}.ID) WHERE {1}.Removed IS NULL AND {3}.ID = {4}",
                            GuruxAMI.Server.AppHost.GetTableName<GXAmiDeviceProfile>(Db),
                            GuruxAMI.Server.AppHost.GetTableName<GXAmiDevice>(Db),
                            GuruxAMI.Server.AppHost.GetTableName<GXAmiDeviceMedia>(Db),
                            GuruxAMI.Server.AppHost.GetTableName<GXAmiDataCollector>(Db),
                            it);
                        list.AddRange(Db.Select<GXAmiDeviceProfile>(query));
                    }
                }
                //Returns all device profiles            
                else
                {
                    list = GetDeviceProfiles(s, Db, 0, 0, request.Preset, request.Protocol, request.Removed);
                }
                //If only latest version is wanted.
                if (!request.All)
                {
                    Dictionary<Guid, GXAmiDeviceProfile> versions = new Dictionary<Guid, GXAmiDeviceProfile>();
                    for (int pos = 0; pos != list.Count; ++pos)
                    {
                        GXAmiDeviceProfile it = list[pos];
                        if (versions.ContainsKey(it.Guid))
                        {
                            GXAmiDeviceProfile tmp = versions[it.Guid];
                            //If older version from the list.
                            if (tmp.ProfileVersion < it.ProfileVersion)
                            {
                                list.Remove(tmp);
                                versions[it.Guid] = it;

                            }
                            else
                            {
                                list.Remove(it);
                            }
                            --pos;
                        }
                        else
                        {
                            versions.Add(it.Guid, it);
                        }
                    }
                }

                //Get allowed mediatypes and device parameters.
                foreach (GXAmiDeviceProfile it in list)
                {
                    it.AllowedMediaTypes = Db.Select<GXAmiMediaType>(q => q.DeviceProfileId == it.Id).ToArray();
                    List<GXAmiParameterTemplate> list2 = Db.Select<GXAmiParameterTemplate>(q => q.ParentID == it.Id << 16);
                    it.Parameters = list2.ConvertAll<GXAmiParameter>(new Converter<GXAmiParameterTemplate, GXAmiParameter>(p => p.ToParameter())).ToArray();
                    //Get possible values for the parameter.
                    foreach (GXAmiParameter param in it.Parameters)
                    {
                        param.Values = Db.Select<GXAmiValueItem>(q => q.ParameterId == param.TemplateId).ToArray();
                    }
                }
                return new GXDeviceProfilesResponse(list.ToArray());
            }
		}

        public GXDeviceProfilesDataResponse Post(GXDeviceProfilesDataRequest request)
        {
            lock (Db)
            {
                byte[] data = null;
                if (request.DeviceId != 0)
                {
                    List<GXAmiDevice> devices = Db.Select<GXAmiDevice>(q => q.Id == request.DeviceId);
                    if (devices.Count != 1)
                    {
                        throw new ArgumentException("Access denied.");
                    }
                    data = JoinPackets(Db, devices[0].ProfileId);
                }
                else if (request.DeviceProfilesGuid != Guid.Empty)
                {
                    List<GXAmiDeviceProfile> templates = Db.Select<GXAmiDeviceProfile>(q => q.Guid == request.DeviceProfilesGuid);
                    ulong id = 0;
                    int ver = 0;
                    if (templates.Count != 1)
                    {
                        foreach (GXAmiDeviceProfile it in templates)
                        {
                            //Get wanted versiom.
                            if (it.ProfileVersion == request.ProfileVersion ||
                                //Get newest version.
                                (request.ProfileVersion == 0 && it.ProfileVersion > ver))
                            {
                                id = it.Id;
                                ver = it.ProfileVersion;
                            }
                        }
                    }
                    else
                    {
                        id = templates[0].Id;
                    }
                    data = JoinPackets(Db, id);
                }
                else
                {
                    throw new ArgumentException("Access denied.");
                }
                return new GXDeviceProfilesDataResponse(data);
            }
        }
        

        /// <summary>
        /// Create new device from the device template.
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        public GXCreateDeviceResponse Get(GXCreateDeviceRequest request)
        {
            lock (Db)
            {
                //Create devices from the device templates.
                List<GXAmiDevice> devices = new List<GXAmiDevice>();
                foreach (ulong id in request.Ids)
                {
                    List<GXAmiDeviceProfile> tmp = Db.Select<GXAmiDeviceProfile>(q => q.Id == id);
                    if (tmp.Count != 1)
                    {
                        throw new ArgumentException("Access denied.");
                    }
                    GXAmiDevice dev = tmp[0] as GXAmiDevice;
                    dev.Guid = Guid.NewGuid();
                    dev.Id = 0;
                    dev.ProfileId = id;
                    List<GXAmiParameterTemplate> list = Db.Select<GXAmiParameterTemplate>(q => q.ParentID == id << 16);
                    dev.Parameters = list.ConvertAll<GXAmiParameter>(new Converter<GXAmiParameterTemplate, GXAmiParameter>(p => p.ToParameter())).ToArray();
                    devices.Add(dev);
                }
                return new GXCreateDeviceResponse(devices.ToArray());
            }
        }
       
        /// <summary>
        /// Remove selected device template
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
		public GXDeviceProfilesDeleteResponse Post(GXDeviceProfilesDeleteRequest request)
		{
            List<GXEventsItem> events = new List<GXEventsItem>();
            IAuthSession s = this.GetSession(false);
            int id = Convert.ToInt32(s.Id);
            if (id == 0)
            {
                throw new ArgumentException("Remove failed. Invalid session ID.");
            }
            bool superAdmin = GuruxAMI.Server.GXBasicAuthProvider.IsSuperAdmin(s);
            lock (Db)
            {
                foreach (ulong it in request.DeviceProfileIDs)
                {
                    if (it == 0)
                    {
                        throw new ArgumentException("ID is required");
                    }
                    if (!superAdmin && GetDeviceProfiles(s, Db, id, 0, false, null, false).Count == 0)
                    {
                        throw new ArgumentException("Access denied.");
                    }
#if !SS4
                    GXAmiDeviceProfile dt = Db.GetById<GXAmiDeviceProfile>(it);
#else
                    GXAmiDeviceProfiles dt = Db.SingleById<GXAmiDeviceProfiles>(it);                    
#endif                                                
                    if (request.Permanently)
                    {
                        Db.DeleteById<GXAmiDeviceProfile>(it);
                    }
                    else
                    {
                        Db.UpdateOnly(new GXAmiDeviceProfile { Removed = DateTime.Now }, p => p.Removed, p => p.Id == it);
                    }
                    events.Add(new GXEventsItem(ActionTargets.DeviceProfile, Actions.Remove, dt));
                }
            }
            AppHost host = this.ResolveService<AppHost>();
            host.SetEvents(Db, this.Request, 0, events);
            return new GXDeviceProfilesDeleteResponse();
        }
	}
}
