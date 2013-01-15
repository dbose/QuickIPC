using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Diagnostics;
using WebAstra.Application.Diagnostics.Logging;

namespace WebAstra.ServiceProcess.Marshaller.QuickIPC
{
    /// <summary>
    /// IPC event delegate
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="eventArgs"></param>
    public delegate void IpcEventHandler(object sender, TextualEventArgs eventArgs);
    
    public enum Context
    {
        /// <summary>
        /// Server context
        /// </summary>
        Server = 0,
        
        /// <summary>
        /// Client context
        /// </summary>
        Client
    }
    
    public class IpcService
    {
        const int MAX_LENGTH = 1024;
        
        private Thread m_ListenerThread;
        private readonly string m_MappedMemoryName;
        private readonly string m_NamedEventName;
        private readonly string m_NamedEventBuddy;
        public event IpcEventHandler IpcEvent;
        private EventWaitHandle m_NamedEventHandle;
        private EventWaitHandle m_NamedEventBuddyHandle;
        private Context m_Context;
        private EventWaitHandle m_Terminated;
        private object m_SyncRoot;

        public IpcService(Context context)
            : this(context,
                    @"Global\QuickIPC-53B088DF-C8A2-4fff-BA70-DDA696778E09",
                    @"Global\QuickIPCEvent-53B088DF-C8A2-4fff-BA70-DDA696778E09",
                    @"Global\QuickIPCEvent-Buddy-53B088DF-C8A2-4fff-BA70-DDA696778E09")
        {}

        private IpcService(Context context, string pMappedMemoryName, string pNamedEventName, string pNamedEventBuddy)
        {
            m_Context = context;
            m_MappedMemoryName = pMappedMemoryName;
            m_NamedEventName = pNamedEventName;
            m_NamedEventBuddy = pNamedEventBuddy;
            m_SyncRoot = new object();

            if (m_Context == Context.Server)
            {
                m_NamedEventHandle = CreateEvent(m_NamedEventName);
                m_NamedEventBuddyHandle = CreateEvent(m_NamedEventBuddy);
            }
            else
            {
                m_NamedEventHandle = GetEvent(m_NamedEventName);
                m_NamedEventBuddyHandle = GetEvent(m_NamedEventBuddy);
            }
        }

        public void Init()
        {
            m_ListenerThread = new Thread(new ThreadStart(listenUsingNamedEventsAndMemoryMappedFiles));
            m_ListenerThread.Name = "IPCListener";
            m_ListenerThread.IsBackground = true;
            m_ListenerThread.Start();
        }
        
        public void UnInit()
        {
            if (m_Terminated != null)
            {
                m_Terminated.Set();
                m_ListenerThread.Join(5000);
            }
        }
        
        /// <summary>
        /// Creates an global event of specified name having empty DACL (again not NULL DACL)
        /// </summary>
        /// <param name="mNamedEventName"></param>
        /// <returns></returns>
        private EventWaitHandle CreateEvent(string mNamedEventName)
        {
           EventWaitHandle handle = null;
            
            try
            {
                //Create a security object that grants no access.
                EventWaitHandleSecurity mSec = new EventWaitHandleSecurity();                
                mSec.AddAccessRule(new EventWaitHandleAccessRule(new SecurityIdentifier(WellKnownSidType.WorldSid, null),
                                                                 EventWaitHandleRights.FullControl,
                                                                 AccessControlType.Allow));

                bool createdNew;
                handle = new EventWaitHandle(false,
                                             EventResetMode.ManualReset,
                                             mNamedEventName,
                                             out createdNew,
                                             mSec);
            }
            catch(WaitHandleCannotBeOpenedException whcboe)
            {
                EventLog.WriteEntry("IpcService",
                                    string.Format("IpcService::CreateEvent: WaitHandle cannot be opened: {0}", whcboe),
                                    EventLogEntryType.Error);
            }
            catch(UnauthorizedAccessException uae)
            {
                EventLog.WriteEntry("IpcService",
                                    string.Format("IpcService::CreateEvent: Unauthorized Access: {0}", uae),
                                    EventLogEntryType.Error);
            }
            catch(Exception ex)
            {
                EventLog.WriteEntry("IpcService",
                                    string.Format("IpcService::CreateEvent: Error while creating event {0}", ex),
                                    EventLogEntryType.Error);
            }

            return handle;
        }

        private EventWaitHandle GetEvent(string mNamedEventName)
        {
            EventWaitHandle ewh = null;
            try
            {
#if !DEBUG
                ewh = EventWaitHandle.OpenExisting(mNamedEventName,
                                                   EventWaitHandleRights.FullControl);
#endif
            }
            catch (UnauthorizedAccessException ex)
            {
                EventLog.WriteEntry("IpcService",
                                    string.Format("IpcService::GetEvent: Querying event {0}", mNamedEventName), 
                                    EventLogEntryType.Error);
            }

            return ewh;
        }

        private void listenUsingNamedEventsAndMemoryMappedFiles()
        {
            if (m_NamedEventHandle == null)
            {
                EventLogger.WriteEntry("Application",
                                       string.Format(
                                           "IpcService::listenUsingNamedEventsAndMemoryMappedFiles: NULL event"),
                                       EventLogEntryType.Error);
                return;
            }
            
            if (m_NamedEventBuddyHandle == null)
            {
                EventLog.WriteEntry("IpcService",
                                    string.Format("IpcService::listenUsingNamedEventsAndMemoryMappedFiles: NULL event (Buddy)"),
                                    EventLogEntryType.Error);
                return;
            }

            m_Terminated = new EventWaitHandle(false, EventResetMode.ManualReset);
            EventWaitHandle[] waitObjects = 
                new EventWaitHandle[] { m_Terminated, m_NamedEventHandle };
            
            try
            {
                while(true)
                {
                    //Waits on "Ready to read?"
                    int index = EventWaitHandle.WaitAny(waitObjects, 
                                                        Timeout.Infinite,false);
                    if (index == 0)
                    {
                        break;
                    }
                    
                    try
                    {
                        //Read data
                        string data = Peek();
                        if (IpcEvent != null)
                        {
                            IpcEvent(this, new TextualEventArgs(data));
                        }
                    }
                    catch(Exception ex)
                    {
                        EventLog.WriteEntry("IpcService",
                                    string.Format("IpcService::listenUsingNamedEventsAndMemoryMappedFiles: Error: {0}", ex),
                                    EventLogEntryType.Error);
                    }
                    finally
                    {
                        m_NamedEventHandle.Reset();

                        //Signals "Read done"
                        m_NamedEventBuddyHandle.Set();
                    }
                }
            }
            finally
            {
                if (m_NamedEventHandle != null)
                {
                    m_NamedEventHandle.Set();
                    m_NamedEventHandle.Close();
                }
                
                if (m_NamedEventBuddyHandle != null)
                {
                    m_NamedEventBuddyHandle.Set();
                    m_NamedEventBuddyHandle.Close();
                }

                m_Terminated.Close();
            }
        }

        public void Poke(string format, params object[] args)
        {
            Poke(string.Format(format, args));
        }

        public void Poke(string somedata)
        {
            lock(m_SyncRoot)
            {
                using (MemoryMappedFileStream fs = new MemoryMappedFileStream(m_MappedMemoryName,
                                                                          MAX_LENGTH,
                                                                          MemoryProtection.PageReadWrite))
                {
                    fs.MapViewToProcessMemory(0, MAX_LENGTH);
                    fs.Write(Encoding.ASCII.GetBytes(somedata + "\0"), 0, somedata.Length + 1);
                }

                //
                //Signal the "Please Read" event and waits on "Read Done"
                EventWaitHandle.SignalAndWait(m_NamedEventHandle, 
                                              m_NamedEventBuddyHandle, 
                                              20000, 
                                              false);
                m_NamedEventBuddyHandle.Reset();
            }
        }

        public string Peek()
        {
            byte[] buffer;
            using (MemoryMappedFileStream fs = new MemoryMappedFileStream(m_MappedMemoryName, 
                                                                          MAX_LENGTH, 
                                                                          MemoryProtection.PageReadWrite))
            {
                fs.MapViewToProcessMemory(0, MAX_LENGTH);
                buffer = new byte[MAX_LENGTH];
                fs.Read(buffer, 0, buffer.Length);
            }
            string readdata = Encoding.ASCII.GetString(buffer, 0, buffer.Length);
            return readdata.Substring(0, readdata.IndexOf('\0'));
        }

        private bool mDisposed = false;

        public void Dispose()
        {
            if (!mDisposed)
            {
                try
                {
                    if (m_NamedEventHandle != null)
                    {
                        m_NamedEventHandle.Close();
                        m_NamedEventHandle = null;
                    }

                    if (m_ListenerThread != null)
                    {
                        m_ListenerThread.Abort();
                        m_ListenerThread = null;
                    }

                    mDisposed = true;
                    GC.SuppressFinalize(this);
                }
                catch
                {
                }
            }
        }

        ~IpcService()
        {
            Dispose();
        }

    }

}
