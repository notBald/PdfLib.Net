using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace PdfLib.Compose.Layout
{
    /// <summary>
    /// A style size
    /// </summary>
    public struct cSize
    {
        public readonly float Value;
        public readonly cUnit Unit;
        public double UnitSize
        {
            get
            {
                switch (Unit)
                {
                    case cUnit.Pixels:
                        return Value;

                    case cUnit.Points:
                        return Value * 16d / 12;

                    case cUnit.Milimeters:
                        return Value * 3.78;

                    case cUnit.Centimeters:
                        return Value * 37.8;

                    default:
                        throw new NotSupportedException();
                }
            }
        }
        internal bool IsFirm { get { return Unit > cUnit.Auto && Unit < cUnit.Precentage; } }
        public cSize(double val)
        {
            Value = (float) val;
            Unit = cUnit.Pixels;
        }
        public cSize(float val)
        {
            Value = val;
            Unit = cUnit.Pixels;
        }
        public cSize(float val, cUnit unit)
        {
            Value = val;
            Unit = unit;
        }
        public cSize Max(float max)
        {
            return (max < Value) ? this : new cSize(max, Unit);
        }
        public static bool operator ==(cSize p1, cSize p2)
        {
            return p1.Value == p2.Value && p1.Unit == p2.Unit;
        }
        public static bool operator !=(cSize p1, cSize p2)
        {
            return p1.Value != p2.Value || p1.Unit != p2.Unit;
        }
        public static cSize operator +(cSize p, cSize v)
        {
            if (p.Unit != v.Unit)
                throw new PdfNotSupportedException();
            return new cSize(p.Value + v.Value, p.Unit);
        }
        public static cSize operator -(cSize p, cSize v)
        {
            if (p.Unit != v.Unit)
                throw new PdfNotSupportedException();
            return new cSize(p.Value - v.Value, p.Unit);
        }
        public override bool Equals(object obj)
        {
            return (obj is cSize) ? this == (cSize)obj : false;
        }
        public override string ToString()
        {
            switch (Unit)
            {
                case cUnit.Pixels: return string.Format("{0} px", Value);
                case cUnit.Precentage: return string.Format("{0} %", Value);
            }
            return Value.ToString();
        }
        public override int GetHashCode()
        {
            unchecked
            {
                int hash = 17;

                hash = hash * 23 + Value.GetHashCode();
                hash = hash * 23 + Unit.GetHashCode();

                return hash;
            }
        }
    }

    public enum cUnit
    {
        Auto = -1,
        Pixels,
        Points,
        Milimeters,
        Centimeters,
        Precentage
    }
}
