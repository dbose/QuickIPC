using System;
using System.Collections.Generic;
using System.Text;
using WebAstra.Common;

namespace WebAstra.ServiceProcess.Marshaller.QuickIPC
{
    public class TextualEventArgs : WebAstraEventArgs
    {
        private string m_Data;
        
        public TextualEventArgs(string data)
        {
            m_Data = data;
        }

        public string Data
        {
            get { return m_Data; }
        }
    }
}
