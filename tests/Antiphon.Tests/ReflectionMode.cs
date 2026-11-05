// Tests are discovered by reflection: the csproj sets EnableTUnitSourceGeneration=false, so no
// generated metadata exists and every run, with or without --reflection, must use this mode.
[assembly: TUnit.Core.ReflectionMode]
