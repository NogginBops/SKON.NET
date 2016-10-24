﻿#region LICENCE
//-----------------------------------------------------------------------
// <copyright file="SKONObject.cs" company="SpaceKrakens">
//     MIT Licence
//     Copyright (C) 2016 SpaceKrakens
// </copyright>
//-----------------------------------------------------------------------
#endregion

namespace SKON.SKEMA
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using System.Text.RegularExpressions;
    using ValueType = ValueType;
    
    public enum SKEMAType
    {
        REFERENCE = -1,
        ANY,
        STRING,
        INTEGER,
        FLOAT,
        BOOLEAN,
        DATETIME,
        MAP,
        ARRAY
    }

    public class SKEMAObject
    {
        //TODO: Add modification methods like Add, Remove and Getters for values

        public static SKEMAObject Any => new SKEMAObject(SKEMAType.ANY);

        public static SKEMAObject String => new SKEMAObject(SKEMAType.STRING);

        public static SKEMAObject Integer => new SKEMAObject(SKEMAType.INTEGER);

        public static SKEMAObject Float => new SKEMAObject(SKEMAType.FLOAT);

        public static SKEMAObject Boolean => new SKEMAObject(SKEMAType.BOOLEAN);

        public static SKEMAObject DateTime => new SKEMAObject(SKEMAType.DATETIME);

        private SKEMAType type;
        
        private Dictionary<string, SKEMAObject> mapSKEMA;

        private bool loose;
        
        private SKEMAObject arraySKEMA;

        private string reference;
        
        private SKEMAObject(SKEMAType type)
        {
            this.type = type;

            if (this.type == SKEMAType.MAP)
            {
                mapSKEMA = new Dictionary<string, SKEMAObject>();
            }
            else if (this.type == SKEMAType.ARRAY)
            {
                arraySKEMA = new SKEMAObject(SKEMAType.ANY);
            }
        }
        
        public SKEMAObject(Dictionary<string, SKEMAObject> mapSKEMA, bool loose = false)
        {
            this.type = SKEMAType.MAP;

            this.mapSKEMA = mapSKEMA;

            this.loose = loose;
        }

        public SKEMAObject(SKEMAObject arraySKEMA)
        {
            type = SKEMAType.ARRAY;

            this.arraySKEMA = arraySKEMA;
        }

        public SKEMAObject(string reference)
        {
            this.type = SKEMAType.REFERENCE;
            this.reference = reference;
        }

        public SKEMAType Type => type;
        
        public bool ResolveReferences(Dictionary<string, SKEMAObject> definitions)
        {
            if ((this.Type == SKEMAType.MAP || this.Type == SKEMAType.ARRAY) == false)
            {
                throw new InvalidOperationException("Can only resolve references for SKEMAObejcts of type MAP and ARRAY!");
            }

            // TODO: Find all strongly connected components.

            // TODO: substiture all references with their definition.

            throw new NotImplementedException();

            return false;
        }

        public bool Valid(SKONObject obj)
        {
            if (this.type != SKEMAType.ANY && (int)this.type != (int)obj.Type)
            {
                return false;
            }

            switch (this.type)
            {
                case SKEMAType.ANY:
                case SKEMAType.STRING:
                case SKEMAType.INTEGER:
                case SKEMAType.FLOAT:
                case SKEMAType.BOOLEAN:
                case SKEMAType.DATETIME:
                    return true;
                case SKEMAType.MAP:
                    if (loose == false && mapSKEMA.Keys.Count != obj.Keys.Count)
                    {
                        return false;
                    }
                    
                    if (obj.AllPresent(mapSKEMA.Keys) == false)
                    {
                        return false;
                    }
                    
                    foreach (string key in mapSKEMA.Keys)
                    {
                        if (mapSKEMA[key].Valid(obj[key]) == false)
                        {
                            return false;
                        }
                    }

                    return true;
                case SKEMAType.ARRAY:
                    foreach (SKONObject value in obj.Values)
                    {
                        if (arraySKEMA.Valid(value) == false)
                        {
                            return false;
                        }
                    }
                    return true;
                default:
                    return false;
            }
        }
    }
}
