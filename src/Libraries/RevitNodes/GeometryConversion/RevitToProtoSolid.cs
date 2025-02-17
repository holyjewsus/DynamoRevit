﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using Autodesk.DesignScript.Geometry;
using Autodesk.DesignScript.Runtime;
using Autodesk.Revit.DB;

using Edge = Autodesk.Revit.DB.Edge;
using Element = Revit.Elements.Element;
using Face = Autodesk.Revit.DB.Face;
using Point = Autodesk.DesignScript.Geometry.Point;
using Solid = Autodesk.DesignScript.Geometry.Solid;
using UV = Autodesk.DesignScript.Geometry.UV;

namespace Revit.GeometryConversion
{
    [SupressImportIntoVM]
    public static class RevitToProtoSolid
    {
        public static Autodesk.DesignScript.Geometry.Solid ToProtoType(this Autodesk.Revit.DB.Solid solid, 
            bool performHostUnitConversion = true)
        {
            var faces = solid.Faces;
            var srfs = new List<Surface>();
            foreach (Face face in faces)
            {
                srfs.AddRange(face.ToProtoType(false));
            }

            Solid converted = null;
            try
            {
                converted = Solid.ByJoinedSurfaces(srfs);
            }
            catch(Exception)
            {
                return null;
            }
            finally
            {
                srfs.ForEach(x => x.Dispose());
                srfs.Clear();
            }

            if (converted == null)
                return null;

            if (performHostUnitConversion)
                UnitConverter.ConvertToDynamoUnits(ref converted);

            return converted;
        }
    }
}
