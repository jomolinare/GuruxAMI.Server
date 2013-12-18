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
using System;
using GuruxAMI.Common;
using ServiceStack.OrmLite;
using System.Data;
using System.Net.NetworkInformation;
using System.Collections.Generic;
using GuruxAMI.Server;

namespace GuruxAMI.Service
{
    /// <summary>
    /// Service handles action functionality.
    /// </summary>
	[Authenticate]
    internal class GXManagementService : ServiceStack.ServiceInterface.Service
	{
        public static string GetMACAddress()
        {
            NetworkInterface[] nis = NetworkInterface.GetAllNetworkInterfaces();
            foreach (NetworkInterface adapter in nis)
            {
                if (adapter.OperationalStatus == OperationalStatus.Up)
                {
                    return adapter.GetPhysicalAddress().ToString();
                }
            }
            return null;
        }

        internal static void CreateTable<T>(IDbConnection Db, ref int index, ProgressEventHandler progress) where T : new()
        {
            Db.CreateTable<T>(false);
            if (progress != null)
            {
                progress(++index, 40);
            }
        }

        /// <summary>
        /// Create default tables and set initial settings.
        /// </summary>
        /// <param name="Db"></param>
        static public void InitializeDB(IDbConnection Db, ProgressEventHandler progress, string userName, string password)
        {
            try
            {
                int index = 0;
                CreateTable<GXAmiSettings>(Db, ref index, progress);
                CreateTable<GXAmiUser>(Db, ref index, progress);
                CreateTable<GXAmiUserGroup>(Db, ref index, progress);
                CreateTable<GXAmiUserGroupUser>(Db, ref index, progress);
                CreateTable<GXAmiDeviceGroup>(Db, ref index, progress);
                CreateTable<GXAmiDeviceTemplate>(Db, ref index, progress);
                CreateTable<GXAmiDevice>(Db, ref index, progress);
                CreateTable<GXAmiMediaType>(Db, ref index, progress);                
                CreateTable<GXAmiDeviceGroup>(Db, ref index, progress);
                CreateTable<GXAmiUserGroupDeviceGroup>(Db, ref index, progress);
                CreateTable<GXAmiDeviceGroupDevice>(Db, ref index, progress);                
                CreateTable<GXAmiParameterTemplate>(Db, ref index, progress);
                CreateTable<GXAmiParameter>(Db, ref index, progress);
                CreateTable<GXAmiPropertyTemplate>(Db, ref index, progress);
                CreateTable<GXAmiProperty>(Db, ref index, progress);
                CreateTable<GXAmiCategoryTemplate>(Db, ref index, progress);
                CreateTable<GXAmiCategory>(Db, ref index, progress);
                CreateTable<GXAmiTableTemplate>(Db, ref index, progress);
                CreateTable<GXAmiDataTable>(Db, ref index, progress);
                CreateTable<GXAmiDataRow>(Db, ref index, progress);
                CreateTable<GXAmiUserGroupDeviceTemplate>(Db, ref index, progress);
                CreateTable<GXAmiDataCollector>(Db, ref index, progress);
                CreateTable<GXAmiDataCollectorDevice>(Db, ref index, progress);
                CreateTable<GXAmiDataCollectorUserGroup>(Db, ref index, progress);
                CreateTable<GXAmiTask>(Db, ref index, progress);
                CreateTable<GXAmiTaskLog>(Db, ref index, progress);
                CreateTable<GXAmiSystemError>(Db, ref index, progress);
                CreateTable<GXAmiDeviceError>(Db, ref index, progress);
                CreateTable<GXAmiUserActionLog>(Db, ref index, progress);
                CreateTable<GXAmiDeviceTemplateDataBlock>(Db, ref index, progress);
                CreateTable<GXAmiLatestValue>(Db, ref index, progress);
                CreateTable<GXAmiValueLog>(Db, ref index, progress);
                CreateTable<GXAmiScheduleTarget>(Db, ref index, progress);
                CreateTable<GXAmiSchedule>(Db, ref index, progress);
                CreateTable<GXAmiDataCollectorParameter>(Db, ref index, progress);
                CreateTable<GXAmiDataCollectorError>(Db, ref index, progress);
                CreateTable<GXAmiTaskData>(Db, ref index, progress);
                CreateTable<GXAmiTrace>(Db, ref index, progress);
                CreateTable<GXAmiTraceData>(Db, ref index, progress);

                //Do not change settings values because they are unique.
                Db.Insert(new GXAmiSettings("DeviceID", "0"));
                Db.Insert(new GXAmiSettings("Version", "1"));
                if (progress != null)
                {
                    progress(++index, 40);
                }

                GXAmiUserGroup ug = new GXAmiUserGroup("SuperAdmins");
                ug.Added = DateTime.Now.ToUniversalTime();
                GXAmiUser user = new GXAmiUser(userName, password, UserAccessRights.SuperAdmin);
                user.Added = DateTime.Now.ToUniversalTime();
                user.AccessRights = UserAccessRights.SuperAdmin;
                Db.Insert(ug);
                ug.Id = Db.GetLastInsertId();
                Db.Insert(user);
                user.Id = Db.GetLastInsertId();
                GXAmiUserGroupUser u = new GXAmiUserGroupUser();
                u.Added = DateTime.Now.ToUniversalTime();
                u.UserID = user.Id;
                u.UserGroupID = ug.Id;
                Db.Insert(u);
                if (progress != null)
                {
                    progress(0, 0);
                }
            }
            catch (Exception ex)
            {
                DropTables(Db);
                throw ex;
            }
        }

        public static GXAmiDataCollector AddDataCollector(IDbConnection Db)
        {
            GXAmiDataCollector dc = new GXAmiDataCollector();
            dc.Guid = Guid.NewGuid();            
            dc.MAC = GetMACAddress();
            dc.Added = DateTime.Now.ToUniversalTime();
            Db.Insert(dc);
            dc.Id = (ulong)Db.GetLastInsertId();
            return dc;
        }

        public static void DropTables(IDbConnection Db)
        {
            Db.DropTable<GXAmiTrace>();
            Db.DropTable<GXAmiTraceData>();
            Db.DropTable<GXAmiDataCollectorError>();
            Db.DropTable<GXAmiDataCollectorDevice>();
            Db.DropTable<GXAmiDataCollectorUserGroup>();
            Db.DropTable<GXAmiUserGroupDeviceTemplate>();
            Db.DropTable<GXAmiUserGroupUser>();
            Db.DropTable<GXAmiUserGroupDeviceGroup>();
            Db.DropTable<GXAmiDeviceGroupDevice>();
            Db.DropTable<GXAmiParameterTemplate>();
            Db.DropTable<GXAmiDeviceTemplateDataBlock>();            
            Db.DropTable<GXAmiCategoryTemplate>();
            Db.DropTable<GXAmiCategory>();            
            Db.DropTable<GXAmiDataRow>();
            Db.DropTable<GXAmiTableTemplate>();
            Db.DropTable<GXAmiDataTable>();            
            Db.DropTable<GXAmiPropertyTemplate>();
            Db.DropTable<GXAmiProperty>();
            Db.DropTable<GXAmiDeviceTemplate>();
            Db.DropTable<GXAmiDataCollector>();
            Db.DropTable<GXAmiUserActionLog>();
            Db.DropTable<GXAmiSystemError>();
            Db.DropTable<GXAmiDeviceError>();
            Db.DropTable<GXAmiUserActionLog>();
            Db.DropTable<GXAmiSettings>();
            Db.DropTable<GXAmiDeviceGroup>();            
            Db.DropTable<GXAmiUser>();
            Db.DropTable<GXAmiUserGroup>();
            Db.DropTable<GXAmiTaskData>();
            Db.DropTable<GXAmiTask>();
            Db.DropTable<GXAmiTaskLog>();
            Db.DropTable<GXAmiParameter>();
            Db.DropTable<GXAmiLatestValue>();
            Db.DropTable<GXAmiValueLog>();
            Db.DropTable<GXAmiMediaType>();
            Db.DropTable<GXAmiDevice>();
            Db.DropTable<GXAmiSchedule>();
            Db.DropTable<GXAmiScheduleTarget>();
            Db.DropTable<GXAmiDataCollectorParameter>();            
        }

        /// <summary>
        /// Drop tables.
        /// </summary>
        /// <param name="request"></param>
        /// <returns></returns>
        public GXDropTablesResponse Post(GXDropTablesRequest request)
		{
            DropTables(Db);
            return new GXDropTablesResponse();
		}

        /// <summary>
        /// Create GuruxAMI database.
        /// </summary>
        public GXCreateTablesResponse Post(GXCreateTablesRequest request)
		{
            InitializeDB(Db, null, request.UserName, request.Password);
            return new GXCreateTablesResponse();
		}

        static public bool IsDatabaseCreated(IDbConnection Db)
        {
            var modelDef = ModelDefinition<GXAmiDataCollectorDevice>.Definition;
            string name = ServiceStack.OrmLite.OrmLiteConfig.DialectProvider.NamingStrategy.GetTableName(modelDef.ModelName);
            return Db.TableExists(name);
        }

        /// <summary>
        /// Is GuruxAMI database created.
        /// </summary>
        public GXIsDatabaseCreatedResponse Post(GXIsDatabaseCreatedRequest request)
		{
            IDbConnectionFactory f = this.TryResolve<IDbConnectionFactory>();
            if (f == null)
            {
                return new GXIsDatabaseCreatedResponse(false);
            }
            return new GXIsDatabaseCreatedResponse(IsDatabaseCreated(Db));
		}        
	}
}
