namespace SKON.Serialization
{
    using System;
    using System.Collections.Generic;
    using System.Text;

    /// <summary>
    /// 
    /// </summary>
    public enum MemberSerialization
    {
        OptOut,
        OptIn,
        Fields
    }

    /// <summary>
    /// 
    /// </summary>
    [AttributeUsage(AttributeTargets.Class | AttributeTargets.Struct | AttributeTargets.Interface, AllowMultiple = false)]
    public class SKONObjectAttribute : Attribute
    {
        public MemberSerialization MemberSerialization { get; set; }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="memberSerialization"></param>
        public SKONObjectAttribute(MemberSerialization memberSerialization)
        {
            MemberSerialization = memberSerialization;
        }
    }
}
