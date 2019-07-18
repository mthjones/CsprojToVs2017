using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Project2015To2017.Definition;
using Project2015To2017.Reading;
using Project2015To2017.Reading.Conditionals;

namespace Project2015To2017.Transforms
{
	public sealed class AddModifiedDefaultPropertiesTransformation : ILegacyOnlyProjectTransformation
	{
		public void Transform(Project definition)
		{
			// The default value for Prefer32Bit was changed in the new SDK for projects that:
			//  1) Target .NET Framework >= v4.0
			//  2) Output an executable
			if (ProjectIsExecutable(definition) && definition.Property("Prefer32Bit") == null)
			{
				var element = new XElement("Prefer32Bit", "true");
				definition.PropertyGroups.First().Add(element);
			}
		}

		private static bool ProjectIsExecutable(Project definition)
		{
			var outputType = definition.Property("OutputType").Value.ToLowerInvariant();
			switch( outputType ) {
				case "exe":
				case "winexe":
				case "appcontainerexe":
				case "":
					return true;
				default:
					return false;
			}
		}
	}
}
