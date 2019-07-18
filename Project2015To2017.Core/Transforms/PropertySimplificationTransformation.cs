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
	public sealed class PropertySimplificationTransformation : ILegacyOnlyProjectTransformation
	{
		private static readonly string[] IgnoreProjectNameValues =
		{
			"$(MSBuildProjectName)",
			"$(ProjectName)"
		};

		private static readonly string[] KnownTestFrameworkIds =
		{
			"Microsoft.NET.Test.Sdk",
			"xUnit.Core",
			"xUnit",
			"NUnit",
		};

		public void Transform(Project definition)
		{
			var msbuildProjectName = definition.FilePath?.Name;
			if (!string.IsNullOrEmpty(msbuildProjectName))
			{
				msbuildProjectName = Path.GetFileNameWithoutExtension(msbuildProjectName);
			}
			else
			{
				// That's actually not what MSBuild does, but it is done to simplify tests
				// and has a incredibly low probability of being triggered on real projects
				// (no project file? empty project filename? seriously?)
				msbuildProjectName = definition.ProjectName;
			}

			// special case handling for when condition-guarded props override global props not set to their defaults
			var globalOverrides = new Dictionary<string, string>();
			foreach (var group in definition.UnconditionalGroups())
			{
				RetrieveGlobalOverrides(group, globalOverrides);
			}

			if (string.IsNullOrEmpty(definition.ProjectName))
			{
				if (!globalOverrides.TryGetValue("ProjectName", out var projectName))
					if (!globalOverrides.TryGetValue("AssemblyName", out projectName))
						projectName = msbuildProjectName;
				definition.ProjectName = projectName;
			}

			foreach (var propertyGroup in definition.PropertyGroups)
			{
				FilterUnneededProperties(definition, propertyGroup, globalOverrides, msbuildProjectName);
			}
		}

		private static void FilterUnneededProperties(Project project,
			XElement propertyGroup,
			IDictionary<string, string> globalOverrides,
			string msbuildProjectName)
		{
			var removeQueue = new List<XElement>();

			foreach (var child in propertyGroup.Elements())
			{
				var parentCondition = propertyGroup.Attribute("Condition")?.Value.Trim() ?? "";
				var hasParentCondition = parentCondition.Length > 1; // no sane condition is 1 char long
				var parentConditionEvaluated =
					ConditionEvaluator.GetNonAmbiguousConditionContracts(parentCondition);
				var parentConditionHasPlatform =
					parentConditionEvaluated.TryGetValue("Platform", out var parentConditionPlatform);
				var parentConditionHasConfiguration =
					parentConditionEvaluated.TryGetValue("Configuration", out var parentConditionConfiguration);
				var parentConditionPlatformLower = parentConditionPlatform?.ToLowerInvariant();
				var parentConditionConfigurationLower = parentConditionConfiguration?.ToLowerInvariant();
				var isDebugOnly = parentConditionHasConfiguration && parentConditionConfigurationLower == "debug";
				var isReleaseOnly = parentConditionHasConfiguration &&
				                    parentConditionConfigurationLower == "release";

				var tagLocalName = child.Name.LocalName;
				var valueLower = child.Value.ToLowerInvariant();
				var valueLowerTrim = valueLower.Trim();
				var emptyValue = valueLowerTrim.Length == 0;
				var hasGlobalOverride = globalOverrides.TryGetValue(tagLocalName, out var globalOverride);
				var globalOverrideLower = globalOverride?.ToLowerInvariant();

				// Regex is the easiest way to replace string between two unknown chars preserving both as is
				// so that bin\Debug\net472 is turned into bin\$(Configuration)\net472
				// and bin/Debug\net472 is turned into bin/$(Configuration)\net472, preserving path separators
				var configurationPathRegex = parentConditionHasConfiguration
					? new Regex($@"([\\/]){parentConditionConfiguration}([\\/])")
					: null;
				var platformPathRegex = parentConditionHasPlatform
					? new Regex($@"([\\/]){parentConditionPlatform}([\\/])")
					: null;

				var hasCondition = child.PropertyCondition(out var fullCondition);
				var fullState = hasCondition ? ConditionEvaluator.GetConditionState(fullCondition) : null;

				switch (tagLocalName)
				{
					// Legacy Visual Studio version
					case "ProductVersion":
					// VS2013 NuGet bugs workaround
					case "NuGetPackageImportStamp":
					// legacy generic properties
					case "MinimumVisualStudioVersion":
					case "VisualStudioVersion":
					case "OldToolsVersion":
					// legacy frameworks
					case "TargetFrameworkIdentifier" when !hasParentCondition:
					case "TargetFrameworkProfile" when !hasParentCondition && emptyValue:
					// VCS properties
					case "SccProjectName" when emptyValue:
					case "SccLocalPath" when emptyValue:
					case "SccAuxPath" when emptyValue:
					case "SccProvider" when emptyValue:
					// Project properties set to defaults (Microsoft.NET.Sdk)
					case "OutputType" when ValidateDefaultValue("library"):
					case "FileAlignment" when ValidateDefaultValue("512"):
					case "ErrorReport" when ValidateDefaultValue("prompt"):
					case "Deterministic" when ValidateDefaultValue("true"):
					case "WarningLevel" when ValidateDefaultValue("4"):
					case "DebugType" when ValidateDefaultValue("portable"):
					case "ResolveNuGetPackages" when ValidateDefaultValue("false"):
					case "SkipImportNuGetProps" when ValidateDefaultValue("true"):
					case "SkipImportNuGetBuildTargets" when ValidateDefaultValue("true"):
					case "RestoreProjectStyle" when ValidateDefaultValue("packagereference"):
					case "AllowUnsafeBlocks" when ValidateDefaultValue("false"):
					case "TreatWarningsAsErrors" when ValidateDefaultValue("false"):
					case "Prefer32Bit" when ValidateDefaultValue("false"):
					case "SignAssembly" when ValidateDefaultValue("false"):
					case "DelaySign" when ValidateDefaultValue("false"):
					case "GeneratePackageOnBuild" when ValidateDefaultValue("false"):
					case "PackageRequireLicenseAcceptance" when ValidateDefaultValue("false"):
					case "DebugSymbols" when ValidateDefaultValue("false"):
					case "CheckForOverflowUnderflow" when ValidateDefaultValue("false"):
					case "AppendTargetFrameworkToOutputPath" when ValidateDefaultValue("true"):
					case "AppDesignerFolder" when ValidateDefaultValue("properties"):
					case "DefaultProjectTypeGuid"
						when ValidateDefaultValue("{fae04ec0-301f-11d3-bf4b-00c04f79efbc}"):
					case "DefaultLanguageSourceExtension" when ValidateDefaultValue(".cs"):
					case "Language" when ValidateDefaultValue("C#"):
					case "TargetRuntime" when ValidateDefaultValue("managed"):
					case "Utf8Output" when ValidateDefaultValue("true"):
					case "PlatformName" when ValidateDefaultValue("$(platform)")
					                         ||
					                         (
						                         parentConditionHasPlatform
						                         &&
						                         ValidateDefaultValue(parentConditionPlatformLower)
					                         ):
					case "RestorePackages" when ValidateDefaultValue("true"):
					case "SchemaVersion" when ValidateDefaultValue("2.0"):
					case "AssemblyVersion" when ValidateDefaultVersion():
					case "FileVersion" when ValidateDefaultVersion():
					case "Version" when ValidateDefaultValue("1.0.0"):
					// Conditional platform default values
					case "PlatformTarget" when parentConditionHasPlatform
					                           && child.Value == parentConditionPlatform
					                           && !hasGlobalOverride:
					// Conditional configuration (Debug/Release) default values
					case "DefineConstants" when isDebugOnly
					                            && ValidateDefaultConstants(valueLower, "debug", "trace")
					                            && !hasGlobalOverride:
					case "DefineConstants" when isReleaseOnly
					                            && ValidateDefaultConstants(valueLower, "trace")
					                            && !hasGlobalOverride:
					case "Optimize" when isDebugOnly && valueLower == "false" && !hasGlobalOverride:
					case "Optimize" when isReleaseOnly && valueLower == "true" && !hasGlobalOverride:
					case "DebugSymbols" when isDebugOnly && valueLower == "true" && !hasGlobalOverride:
					// Default project values for Platform and Configuration
					case "Platform" when ValidateEmptyConditionValue()
					                     && valueLower == "anycpu":
					case "Configuration" when ValidateEmptyConditionValue()
					                          && valueLower == "debug":
					case "Platforms" when !hasParentCondition
					                      && ValidateDefaultConstants(valueLower, "anycpu"):
					case "Configurations" when !hasParentCondition
					                           && ValidateDefaultConstants(valueLower, "debug", "release"):
					// Extra ProjectName duplicates
					case "RootNamespace" when IsDefaultProjectNameValued():
					case "AssemblyName" when IsDefaultProjectNameValued():
					case "TargetName" when IsDefaultProjectNameValued():
					case "ProjectGuid" when ProjectGuidMatchesSolutionProjectGuid():
					case "Service" when IncludeMatchesSpecificGuid():
					{
						removeQueue.Add(child);
						break;
					}

				}

				if (parentConditionHasConfiguration || parentConditionHasPlatform)
				{
					switch (tagLocalName)
					{
						case "OutputPath":
						case "IntermediateOutputPath":
						case "DocumentationFile":
						case "CodeAnalysisRuleSet":
						{
							child.Value = ReplaceWithPlaceholders(child.Value);

							break;
						}
					}
				}

				// following local methods will capture parent scope
				bool ValidateDefaultValue(string @default)
				{
					return (!hasParentCondition && valueLower == @default) ||
					       (hasParentCondition && ValidateConditionedDefaultValue(@default));
				}

				bool ValidateConditionedDefaultValue(string @default)
				{
					return (valueLower == @default) && (!hasGlobalOverride || globalOverrideLower == @default);
				}

				bool ValidateDefaultVersion()
				{
					return ValidateDefaultValue("1.0.0.0")
					       || ValidateDefaultValue("1.0.0")
					       || ValidateDefaultValue("1.0")
					       || ValidateDefaultValue("1");
				}

				string ReplaceWithPlaceholders(string value)
				{
					if (parentConditionHasConfiguration)
					{
						value = configurationPathRegex.Replace(value, "$1$(Configuration)$2");
					}

					if (parentConditionHasPlatform)
					{
						value = platformPathRegex.Replace(value, "$1$(Platform)$2");
					}

					return value;
				}

				bool ValidateEmptyConditionValue()
				{
					if (!hasCondition || string.IsNullOrWhiteSpace(fullCondition))
					{
						return true;
					}

					return (fullCondition.Count(x => x == '=') == 2) && fullCondition.Contains("''");
				}

				bool IsDefaultProjectNameValued()
				{
					return ValidateEmptyConditionValue()
					       && (
						       child.Value == msbuildProjectName
						       ||
						       IgnoreProjectNameValues.Contains(child.Value)
					       );
				}

				bool IncludeMatchesSpecificGuid()
				{
					// This is not required as of VS15.7 and above, but we expect VS15.0

					if (!project.PackageReferences.Any(IsKnownTestProvider))
						return false;

					return child.Attribute("Include")?.Value == "{82A7F48D-3B50-4B1E-B82E-3ADA8210C358}";

					bool IsKnownTestProvider(PackageReference x)
					{
						// In theory, we should be checking versions of these test frameworks
						// to see, if they have the fix included.

						return KnownTestFrameworkIds.Contains(x.Id);
					}
				}
			}

			// we cannot remove elements correctly while iterating through elements, 2nd pass is needed
			foreach (var child in removeQueue)
			{
				child.Remove();
			}

			bool ProjectGuidMatchesSolutionProjectGuid()
			{
				return project.Solution != null && project.Solution.ProjectPaths.Any(x => x.ProjectName == project.ProjectName && x.ProjectGuid == project.ProjectGuid);
			}
		}

		/// <summary>
		/// Get all non-conditional properties and their respective values
		/// </summary>
		/// <param name="propertyGroup">Primary unconditional PropertyGroup to be inspected</param>
		/// <param name="globalOverrides"></param>
		/// <returns>Dictionary of properties' keys and values</returns>
		private static void RetrieveGlobalOverrides(XElement propertyGroup, IDictionary<string, string> globalOverrides)
		{
			foreach (var child in propertyGroup.Elements())
			{
				if (!HasEmptyCondition(child))
				{
					continue;
				}

				globalOverrides[child.Name.LocalName] = child.Value.Trim();
			}

			bool HasEmptyCondition(XElement element)
			{
				var conditionAttribute = element.Attribute("Condition");
				if (conditionAttribute == null)
				{
					return true;
				}

				var condition = conditionAttribute.Value.Trim() ?? "";

				// no sane condition is 1 char long
				return condition.Length <= 1;
			}
		}

		private static bool ValidateDefaultConstants(string value, params string[] expected)
		{
			var defines = value.Split(new[] {';'}, StringSplitOptions.RemoveEmptyEntries);
			return Extensions.ValidateSet(defines, expected);
		}
	}
}
