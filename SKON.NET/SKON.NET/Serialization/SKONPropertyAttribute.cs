using System;
using System.Collections.Generic;
using System.Text;

namespace SKON.Serialization
{
    /// <summary>
    /// 
    /// </summary>
    [AttributeUsage(AttributeTargets.Field | AttributeTargets.Property)]
    public class SKONPropertyAttribute : Attribute
    {
        /// <summary>
        /// The name of the SKON map element when serializing.
        /// </summary>
        public string Name;
        
        /// <summary>
        /// 
        /// </summary>
        /// <param name="Name"></param>
        public SKONPropertyAttribute(string Name)
        {
            this.Name = Name;
        }
    }
}
