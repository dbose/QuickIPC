using System;
using System.Collections.Generic;
using System.Text;

namespace QuickIPC
{
    public class TextualEventArgs : EventArgs
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
