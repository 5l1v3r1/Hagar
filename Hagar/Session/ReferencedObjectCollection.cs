using System.Collections.Generic;
using Hagar.Utilities;

namespace Hagar.Session
{
    public class ReferencedObjectCollection
    {
        private readonly Dictionary<uint, object> references = new Dictionary<uint, object>();
        private readonly Dictionary<object, uint> referenceToIdMap = new Dictionary<object, uint>(ReferenceEqualsComparer.Instance);

        public bool TryGetReferencedObject(uint reference, out object value)
        {
            // Reference 0 is always null.
            if (reference == 0)
            {
                value = null;
                return true;
            }

            return this.references.TryGetValue(reference, out value);
        }

        public void RecordReferenceField(object value)
        {
            if (value == null) return;
            this.references.Add(++this.CurrentReferenceId, value);
        }

        public void MarkValueField() => ++this.CurrentReferenceId;

        public bool GetOrAddReference(object value, out uint reference)
        {
            // Null is always at reference 0
            if (value == null)
            {
                reference = 0;
                return true;
            }

            if (this.referenceToIdMap.TryGetValue(value, out reference)) return true;
            
            // Add the reference.
            reference = ++this.CurrentReferenceId;
            this.referenceToIdMap.Add(value, this.CurrentReferenceId);
            return false;
        }

        public Dictionary<uint, object> CopyReferenceTable() => new Dictionary<uint, object>(this.references);

        public uint CurrentReferenceId { get; set; }

        public void Reset()
        {
            this.references.Clear();
            this.referenceToIdMap.Clear();
            this.CurrentReferenceId = 0;
        }
    }
}