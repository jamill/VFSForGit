using System;
using System.Collections.Generic;
using System.Text;

namespace GVFS.Common
{
    public class GVFSException : System.Exception
    {
        /// <summary>
        /// Base exception to indicate a GVFS error
        /// </summary>
        public GVFSException()
            : base()
        {
        }

        public GVFSException(string message)
            : base(message)
        {
        }
    }
}
