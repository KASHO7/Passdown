using System.Reflection;
using Carowinds.Passdown.Application.Associate;
using Carowinds.Passdown.Application.Finalization;
using Carowinds.Passdown.Application.PersonalSync;

namespace Carowinds.Passdown.Application.Tests.Weather;

/// <summary>
/// Phase 37F follow-up: neither the "Include Weather Outlook" device preference nor weather itself is part of any
/// draft, Personal Sync or finalized-snapshot shape (spec §16.6, §6.6).
/// </summary>
public class WeatherOutlookPreferenceBoundaryTests
{
    public static TheoryData<Type> SerializedShapes() => new()
    {
        typeof(AssociateDraftPayload),
        typeof(SyncEnvelope),
        typeof(SyncManifest),
        typeof(FinalizedPassdownSnapshot),
        typeof(FinalizedPassdownSyncPayload),
        typeof(SyncConfiguration),
        typeof(PersonalSyncSettingsSnapshot),
    };

    [Theory]
    [MemberData(nameof(SerializedShapes))]
    public void SerializedShapes_CarryNoWeatherOrWeatherPreference(Type shape)
    {
        var offending = Members(shape, [])
            .Where(name => name.Contains("Weather", StringComparison.OrdinalIgnoreCase))
            .ToArray();

        Assert.Empty(offending);
    }

    /// <summary>Public property names of <paramref name="type"/> and of the application types it contains, recursively.</summary>
    private static IEnumerable<string> Members(Type type, HashSet<Type> seen)
    {
        if (!seen.Add(type) || type.Namespace?.StartsWith("Carowinds.", StringComparison.Ordinal) != true)
        {
            yield break;
        }

        foreach (var property in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            yield return $"{type.Name}.{property.Name}";
            var propertyType = property.PropertyType;
            var inner = propertyType.IsGenericType ? propertyType.GetGenericArguments() : [propertyType];
            foreach (var candidate in inner)
            {
                foreach (var name in Members(Nullable.GetUnderlyingType(candidate) ?? candidate, seen))
                {
                    yield return name;
                }
            }
        }
    }
}
