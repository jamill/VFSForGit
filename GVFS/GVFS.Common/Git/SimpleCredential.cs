using System;
using System.Collections.Generic;
using System.Text;

namespace GVFS.Common.Git
{
    public class SimpleCredential
    {
        public SimpleCredential(string username, string password)
        {
            this.Username = username;
            this.Password = password;
        }

        public string Username { get; }
        public string Password { get; }

        public string BasicAuthString
        {
            get => Convert.ToBase64String(Encoding.ASCII.GetBytes(this.Username + ":" + this.Password));
        }
    }
}
