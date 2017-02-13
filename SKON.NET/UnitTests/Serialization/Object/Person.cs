namespace UnitTests.Serialization.Object
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using SKON.Serialization;
    
    [SKONObject(MemberSerialization.Fields)]
    struct Person
    {
        public string FirstName, LastName;
        public DateTime BirthDate;
        public string Email;

        public Person(string firstName, string lastName, DateTime birthDate, string email)
        {
            this.FirstName = firstName;
            this.LastName = lastName;
            this.BirthDate = birthDate;
            this.Email = email;
        }
    }
}
