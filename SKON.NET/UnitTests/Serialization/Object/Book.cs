namespace UnitTests.Serialization.Object
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Text;
    using System.Threading.Tasks;
    using SKON.Serialization;

    [SKONObject(MemberSerialization.Fields)]
    class Book
    {
        [SKONProperty("Name")]
        string name;

        [SKONProperty("Author")]
        Person author;

        [SKONProperty("Publication_Date")]
        DateTime publicationDate;

        public string Name => name;

        public Person Author => author;

        public DateTime PublicationDate => publicationDate;

        public Book(string name, Person author, DateTime publicationDate)
        {
            this.name = name;
            this.author = author;
            this.publicationDate = publicationDate;
        }
    }
}
