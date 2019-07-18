using System;
using System.Collections.Immutable;
using System.Linq;
using Project2015To2017.Definition;
using Project2015To2017.Transforms;

namespace Project2015To2017.Migrate2017.Transforms
{
	public sealed class UpgradeTestServiceTransformation : ITransformation
	{
		public void Transform(Project definition)
		{
			var removeQueue = definition.ItemGroups
				.ElementsAnyNamespace("Service")
				.Where(x => !string.IsNullOrEmpty(x.Attribute("Include").Value) && string.Equals(x.Attribute("Include").Value, "{82A7F48D-3B50-4B1E-B82E-3ADA8210C358}", StringComparison.OrdinalIgnoreCase))
				.ToImmutableArray();

			foreach (var element in removeQueue)
			{
				element.Remove();
			}
		}
	}
}
