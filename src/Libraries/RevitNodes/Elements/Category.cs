﻿using System;

using Autodesk.DesignScript.Runtime;
using Autodesk.Revit.DB;
using RevitServices.Persistence;

namespace Revit.Elements
{
    public class Category
    {
        #region private constructors

        private Category(Autodesk.Revit.DB.Category category)
        {
            internalCategory = category;
        }

        #endregion

        #region private members

        private readonly Autodesk.Revit.DB.Category internalCategory;

        #endregion

        #region public properties

        /// <summary>
        /// The name of the Category.
        /// </summary>
        public string Name
        {
            get
            {
                return internalCategory.Name;
            }
        }

        /// <summary>
        /// The Id of the category.
        /// </summary>
        public int Id
        {
            get
            {
                return InternalCategory.Id.IntegerValue;
            }
        }

        #endregion

        internal Autodesk.Revit.DB.Category InternalCategory
        {
            get { return internalCategory; }
        }

        #region public static constructors

        /// <summary>
        /// Gets a Revit category by the built-in category name.
        /// </summary>
        /// <param name="name">The built in category name.</param>
        /// <returns></returns>
        public static Category ByName(string name)
        {
            if (name == null)
            {
                throw new ArgumentNullException("name");
            }

            Settings documentSettings = DocumentManager.Instance.CurrentDBDocument.Settings;
            var groups = documentSettings.Categories;
            var builtInCat = (BuiltInCategory)Enum.Parse(typeof(BuiltInCategory), name);
            var category = groups.get_Item(builtInCat);

            if (category == null)
            {
                throw new Exception(Properties.Resources.InvalidCategory);
            }

            return new Category(category);
        }

        #endregion

        public override string ToString()
        {
            return internalCategory != null ? internalCategory.Name : string.Empty;
        }
    }
}
