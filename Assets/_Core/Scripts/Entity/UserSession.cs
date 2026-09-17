using System;
using UnityEngine;

namespace Core.Entity
{
    [Serializable]
    public class UserSession
    {
        public int id;
        public string email;
        public string username;
        public string token;
        public int systemid;
        public int planetid;
    }
}
