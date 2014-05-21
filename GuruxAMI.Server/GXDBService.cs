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

using GuruxAMI.Common;
using GuruxAMI.Service;
using System.Collections.Generic;
using System;
using System.Data;
using ServiceStack.OrmLite;
#if !SS4
#else
using ServiceStack.Data;
#endif

namespace GuruxAMI.Server
{
    public delegate void ProgressEventHandler(int index, int count);

    public class GXWebDBService
    {
        private GXWebAppHost appHost;

        /// <summary>
        /// Create GuruxAMI Web Service.
        /// </summary>
        /// <param name="prefix">table prefix.</param>
        public GXWebDBService(IDbConnectionFactory connectionFactory, string prefix)            
        {
            this.appHost = new GXWebAppHost(connectionFactory, prefix);
            this.appHost.Init();
        }
    }

	public class GXDBService
	{
        private ProgressEventHandler m_OnProgress;
		private GXAppHost appHost;        
		public string Url
		{
			get;
			internal set;
		}

        /// <summary>
        /// Create GuruxAMI Service.
        /// </summary>
        /// <param name="prefix">table prefix.</param>
        public GXDBService(string urlBase, IDbConnectionFactory connectionFactory, string prefix)
        {
            if (this.appHost != null)
            {
                this.appHost.Stop();
            }
            this.Url = urlBase;
            this.appHost = new GXAppHost(connectionFactory, prefix);
            this.appHost.Init();
            this.appHost.Start(this.Url);
        }

        public event ProgressEventHandler OnProgress
        {
            add
            {
                m_OnProgress += value;
            }
            remove
            {
                m_OnProgress -= value;
            }
        }

        /// <summary>
        /// Create database tables.
        /// </summary>
        /// <param name="Db"></param>
        /// <param name="progress"></param>
        /// <param name="userName"></param>
        /// <param name="password"></param>
        static public void CreateTables(IDbConnection Db, ProgressEventHandler progress, string userName, string password)
        {
            GuruxAMI.Service.GXManagementService.InitializeDB(Db, progress, userName, password);            
        }

        public void CreateTables(ProgressEventHandler progress, string userName, string password)
        {
            using (IDbConnection Db = this.appHost.TryResolve<IDbConnectionFactory>().OpenDbConnection())
            {
                GuruxAMI.Service.GXManagementService.InitializeDB(Db, progress, userName, password);
            }            
        }

        /// <summary>
        /// Check are GuruxAMI tables created.
        /// </summary>
        /// <returns></returns>
        public bool IsDatabaseCreated()
        {
            using (IDbConnection Db = this.appHost.TryResolve<IDbConnectionFactory>().OpenDbConnection())
            {
                return GuruxAMI.Service.GXManagementService.IsDatabaseCreated(Db);
            }
        }

        /// <summary>
        /// Update database.
        /// </summary>
        public void Update()
        {
            OrmLiteConnectionFactory f = this.appHost.TryResolve<IDbConnectionFactory>() as OrmLiteConnectionFactory;
            if (f.ConnectionString != null)
            {
                using (IDbConnection Db = f.OpenDbConnection())
                {
                    if (!GuruxAMI.Service.GXManagementService.IsDatabaseCreated(Db))
                    {
                        return;
                    }
                    List<GXAmiSettings> tmp = Db.Select<GXAmiSettings>(q => q.Name == "Version");
                    int version = 0;
                    if (tmp.Count == 1)
                    {
                        version = Convert.ToInt32(tmp[0].Value);
                    }
                    else if (tmp.Count != 0)
                    {
                        throw new Exception("Invalid version.");
                    }
                    if (version == 0)
                    {
                        Db.ExecuteSql("ALTER TABLE Device ADD TraceLevel int(11) AFTER TimeStamp");
                        Db.ExecuteSql("ALTER TABLE DataCollector ADD TraceLevel int(11) AFTER UnAssigned");

                        Db.ExecuteSql("ALTER TABLE Settings MODIFY COLUMN ID int(4) auto_increment");
                        //Drop device foreign key from task and task log tables.                    
                        Db.ExecuteSql("ALTER TABLE DeviceError ADD DataCollectorID bigint(20) AFTER TargetDeviceID");
                        //Drop device foreign key from device error. Taskista tämä...                    
                        Db.ExecuteSql("ALTER TABLE DeviceError DROP FOREIGN KEY FK_DeviceError_Task_TaskID");
                        //Drop device foreign key from task and task log tables.                    
                        Db.ExecuteSql("ALTER TABLE Task DROP FOREIGN KEY FK_Task_Device_TargetDeviceID");
                        Db.ExecuteSql("ALTER TABLE TaskLog DROP FOREIGN KEY FK_TaskLog_Device_TargetDeviceID");
                        //Create DC errors table.
                        Db.CreateTable<GXAmiDataCollectorError>(false);
                        Db.CreateTable<GXAmiTaskData>(false);
                        Db.CreateTable<GXAmiTrace>(false);
                        Db.CreateTable<GXAmiTraceData>(false);
                        Db.Insert(new GXAmiSettings("Version", "1"));
                    }
                    if (version == 1)
                    {
                        Db.CreateTable<GXAmiVisualizer>(false);
                        Db.CreateTable<GXAmiDeviceMedia>(false);
                        tmp[0].Value = "2";
                        Db.Update<GXAmiSettings>(tmp[0]);
                    }
                    if (version <= 2)
                    {
                        Db.ExecuteSql("ALTER TABLE DeviceMedia ADD Disabled boolean AFTER Settings");
                        tmp[0].Value = "3";
                        Db.Update<GXAmiSettings>(tmp[0]);
                    }
                    if (version <= 3)
                    {
                        Db.ExecuteSql("ALTER TABLE Task ADD ReplyID bigint(20) AFTER ID");
                        Db.ExecuteSql("ALTER TABLE TaskLog ADD ReplyID bigint(20) AFTER ID");
                        tmp[0].Value = "4";
                        Db.Update<GXAmiSettings>(tmp[0]);
                    }
                    if (version <= 4)
                    {
                        Db.ExecuteSql("ALTER TABLE Schedule ADD Status int(4)");
                        Db.ExecuteSql("ALTER TABLE Schedule ADD NextRunTine datetime AFTER ScheduleEndTime");
                        Db.ExecuteSql("ALTER TABLE Schedule ADD LastRunTime datetime AFTER NextRunTine");
                        Db.ExecuteSql("ALTER TABLE DeviceMedia MODIFY DataCollectorId bigint(20) NULL");
                        tmp[0].Value = "5";
                        Db.Update<GXAmiSettings>(tmp[0]);
                    }
                    if (version <= 5)
                    {
                        Db.ExecuteSql("ALTER TABLE DeviceError MODIFY DataCollectorId bigint(20) NULL");
                        Db.ExecuteSql("ALTER TABLE DeviceError MODIFY TargetDeviceID bigint(20) NULL");
                        tmp[0].Value = "6";
                        Db.Update<GXAmiSettings>(tmp[0]);
                    }                    
                }
            }
        }       

        static public GuruxAMI.Common.GXAmiDataCollector AddDataCollector(IDbConnection Db)
        {
            return GuruxAMI.Service.GXManagementService.AddDataCollector(Db);
        }

		public void Stop()
		{
			this.appHost.Stop();
            this.appHost.Dispose();
		}
	}
}
