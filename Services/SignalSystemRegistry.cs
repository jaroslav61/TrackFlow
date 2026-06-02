using System;
using System.Collections.Generic;
using System.Linq;
using TrackFlow.Models.Layout;

namespace TrackFlow.Services;

/// <summary>
/// Registratúra zabudovaných návestných sústav.
/// Poskytuje definície sústav, profilov a aspektov pre editor aj runtime.
/// </summary>
public static class SignalSystemRegistry
{
    /// <summary>Všetky zabudované sústavy (read-only).</summary>
    public static IReadOnlyList<SignalSystemDefinition> BuiltinSystems { get; } = BuildBuiltinSystems();

    /// <summary>Vráti zabudovanú sústavu podľa ID, alebo null ak neexistuje.</summary>
    public static SignalSystemDefinition? GetBuiltin(string systemId)
        => BuiltinSystems.FirstOrDefault(s => string.Equals(s.Id, systemId, StringComparison.Ordinal));

    /// <summary>Vráti profily zabudovanej sústavy podľa jej ID.</summary>
    public static IReadOnlyList<SignalProfileDefinition> GetProfiles(string systemId)
    {
        var sys = GetBuiltin(systemId);
        return sys != null ? sys.Profiles : Array.Empty<SignalProfileDefinition>();
    }

    /// <summary>
    /// Vráti profil podľa ID sústavy a ID profilu.
    /// Ak profil neexistuje v zabudovanej sústave, pokúsi sa nájsť v projektovej definícii.
    /// </summary>
    public static SignalProfileDefinition? GetProfile(string systemId, string? profileId)
    {
        if (string.IsNullOrEmpty(profileId)) return null;
        var normalizedProfileId = NormalizeProfileId(profileId);
        return GetBuiltin(systemId)?.Profiles
                   .FirstOrDefault(p => string.Equals(p.Id, normalizedProfileId, StringComparison.Ordinal));
    }

    /// <summary>
    /// Vráti true, ak profil fyzicky podporuje požadovaný aspekt.
    /// Null/unknown profil neblokujeme – volajúci môže uplatniť vlastnú politiku.
    /// </summary>
    public static bool SupportsPhysicalAspect(string? systemId, string? profileId, SignalAspect aspect)
    {
        var normalizedProfileId = NormalizeProfileId(profileId);
        return normalizedProfileId switch
        {
            // Zriaďovacie návestidlo fyzicky zobrazuje len posunovú návesť a Stoj.
            "2-aspect-shunt" => aspect is SignalAspect.Stop or SignalAspect.ShuntingPermitted,

            // Oddielové Ž/Z/R fyzicky nemá dolnú žltú, preto nevie korektne zobraziť
            // rýchlostné aspekty vyžadujúce dolnú žltú.
            "3-aspect" => aspect is not SignalAspect.SlowProceed and not SignalAspect.SlowCaution,

            // Vchodové Z/R/B nemá žltú lampu vôbec.
            "3-aspect-entry" => aspect is SignalAspect.Proceed or SignalAspect.Stop or SignalAspect.ShuntingPermitted,

            // Ostatné profily nechávame kvôli spätnej kompatibilite permissive.
            _ => true
        };
    }

    /// <summary>
    /// Vráti true, ak profil je sémanticky vhodný ako návestidlo vlakovej cesty.
    /// Zriaďovacie profily sa nesmú používať ako route-signály na vlakovej trase.
    /// </summary>
    public static bool SupportsTrainRouteRole(string? systemId, string? profileId)
    {
        var normalizedProfileId = NormalizeProfileId(profileId);
        return normalizedProfileId switch
        {
            "2-aspect-shunt" => false,
            _ => true
        };
    }

    /// <summary>
    /// Spätná kompatibilita: legacy alias pre fyzickú kompatibilitu aspektu.
    /// </summary>
    public static bool SupportsAspect(string? systemId, string? profileId, SignalAspect aspect)
        => SupportsPhysicalAspect(systemId, profileId, aspect);

    /// <summary>
    /// Spätná kompatibilita: legacy alias pre prevádzkovú vhodnosť na vlakovej ceste.
    /// </summary>
    public static bool SupportsTrainRouteUsage(string? systemId, string? profileId)
        => SupportsTrainRouteRole(systemId, profileId);

    /// <summary>
    /// Vyrieši bezpečný náhradný aspekt pre profil, ktorý nevie zobraziť požadovaný aspekt.
    /// Politika: zachovaj požadovaný aspekt, ak je podporovaný; inak uprednostni Stop,
    /// pri čistej predzvesti Caution, následne Caution/ShuntingPermitted/Proceed.
    /// </summary>
    public static SignalAspect ResolveFailSafeAspect(string? systemId, string? profileId, SignalAspect requestedAspect)
    {
        var normalizedProfileId = NormalizeProfileId(profileId);
        return normalizedProfileId switch
        {
            // Predzvesť bez červenej: najbezpečnejší dostupný fallback za Stop je Výstraha.
            "2-aspect" when requestedAspect == SignalAspect.Stop => SignalAspect.Caution,

            // Oddielové Ž/Z/R: dolná žltá neexistuje, preto fallback na hornú žltú = Výstraha.
            "3-aspect" when requestedAspect is SignalAspect.SlowProceed or SignalAspect.SlowCaution => SignalAspect.Caution,

            // Vchodové Z/R/B: bez žltej lampy je fail-safe fallback na Stop.
            "3-aspect-entry" when requestedAspect is SignalAspect.Caution
                or SignalAspect.SlowProceed
                or SignalAspect.SlowCaution
                or SignalAspect.SlowExpect40 => SignalAspect.Stop,

            _ => requestedAspect
        };
    }

    /// <summary>
    /// Vráti definition aspektu podľa ID sústavy, profilu a aspektu.
    /// </summary>
    public static SignalAspectDefinition? GetAspectDef(string systemId, string? profileId, SignalAspect aspect)
    {
        var profile = GetProfile(systemId, profileId);
        return profile?.Aspects.FirstOrDefault(a => a.Aspect == aspect);
    }

    /// <summary>
    /// Runtime rozlíšenie aspektu signálu podľa profilu a stavu bloku.
    /// Centralizované pravidlá pre editor/operation logiku.
    /// </summary>
    public static SignalAspect ResolveRuntimeAspect(string? profileId, bool isOccupied, bool requestYellow)
    {
        var normalizedProfileId = NormalizeProfileId(profileId);

        return normalizedProfileId switch
        {
            // Zriaďovacie: stop=Stop, voľno=ShuntingPermitted.
            "2-aspect-shunt" => isOccupied ? SignalAspect.Stop : SignalAspect.ShuntingPermitted,

            // Cestové: stop=Stop (červená), voľno=ShuntingPermitted (biela).
            // Pozn.: biela tu predstavuje "cesta dovolená" (bez žltej logiky).
            "2-aspect-route" => isOccupied ? SignalAspect.Stop : SignalAspect.ShuntingPermitted,

            // Predzvesť: výstraha=Caution, voľno=Proceed (bez Stop).
            "2-aspect" => isOccupied ? SignalAspect.Caution : SignalAspect.Proceed,

            // Vchodové 3-znakové (Z/R/B): obsadené=Stop, voľné=Proceed,
            // requestYellow použijeme ako požiadavku na bielu (posun / špec. režim).
            "3-aspect-entry" => isOccupied
                ? SignalAspect.Stop
                : requestYellow
                    ? SignalAspect.ShuntingPermitted
                    : SignalAspect.Proceed,

            // Ostatné profily: štandardná stop/caution/go logika.
            _ => isOccupied
                ? SignalAspect.Stop
                : requestYellow
                    ? SignalAspect.Caution
                    : SignalAspect.Proceed
        };
    }

    private static string? NormalizeProfileId(string? profileId)
    {
        if (string.IsNullOrWhiteSpace(profileId))
            return profileId;

        return profileId.EndsWith("-head", StringComparison.Ordinal)
            ? profileId[..^5] + "-aspect"
            : profileId;
    }

    private static List<SignalSystemDefinition> BuildBuiltinSystems()
    {
        return new List<SignalSystemDefinition>
        {
            BuildSlovakSystem()
        };
    }

    /// <summary>Slovenská základná návestná sústava ŽSR/ZSSK.</summary>
    private static SignalSystemDefinition BuildSlovakSystem()
    {
        return new SignalSystemDefinition
        {
            Id = SignalSystemDefinition.DefaultSystemId,
            Name = "Slovenská základná sústava",
            Kind = SignalingSystemKind.Slovak,
            SupportedHeadCounts = new List<int> { 2, 3, 4, 5 },
            Profiles = new List<SignalProfileDefinition>
            {
                // 2 znaky - Predzvesť
                new SignalProfileDefinition
                {
                    Id = "2-aspect",
                    DisplayName = "Predzvesť [🟡🟢]",
                    HeadCount = 2,
                    Aspects = new List<SignalAspectDefinition>
                    {
                        new SignalAspectDefinition
                        {
                            Id = "Caution",
                            DisplayName = "Výstraha (zastav!)",
                            Aspect = SignalAspect.Caution,
                            MarkerAssetName = "signal_sk_2n_caution.png",
                            Color = "#FDD835"
                        },
                        new SignalAspectDefinition
                        {
                            Id = "Go",
                            DisplayName = "Voľno",
                            Aspect = SignalAspect.Proceed,
                            MarkerAssetName = "signal_sk_2n_go.png",
                            Color = "#43A047"
                        }
                    }
                },

                // 2 znaky - Hlavné
                new SignalProfileDefinition
                {
                    Id = "2-aspect-main",
                    DisplayName = "Hlavné [🔴🟢]",
                    HeadCount = 2,
                    Aspects = new List<SignalAspectDefinition>
                    {
                        new SignalAspectDefinition
                        {
                            Id = "Stop",
                            DisplayName = "Stoj!",
                            Aspect = SignalAspect.Stop,
                            MarkerAssetName = "signal_sk_2n_main_stop.png",
                            Color = "#E53935"
                        },
                        new SignalAspectDefinition
                        {
                            Id = "Go",
                            DisplayName = "Voľno",
                            Aspect = SignalAspect.Proceed,
                            MarkerAssetName = "signal_sk_2n_main_go.png",
                            Color = "#43A047"
                        }
                    }
                },

                // 2 znaky - Zriaďovacie
                new SignalProfileDefinition
                {
                    Id = "2-aspect-shunt",
                    DisplayName = "Zriaďovacie [🔵⚪]",
                    HeadCount = 2,
                    Aspects = new List<SignalAspectDefinition>
                    {
                        new SignalAspectDefinition
                        {
                            Id = "GoShunt",
                            DisplayName = "Posun povolený",
                            Aspect = SignalAspect.ShuntingPermitted,
                            MarkerAssetName = "signal_sk_2n_sh_go.png",
                            Color = "#F5F5F5"
                        },
                        new SignalAspectDefinition
                        {
                            Id = "StopShunt",
                            DisplayName = "Posun zakázaný",
                            Aspect = SignalAspect.Stop,
                            MarkerAssetName = "signal_sk_2n_sh_stop.png",
                            Color = "#E53935"
                        }
                    }
                },

                // 2 znaky - Cestové (Červená / Biela)
                new SignalProfileDefinition
                {
                    Id = "2-aspect-route",
                    DisplayName = "Cestové [🔴⚪]",
                    HeadCount = 2,
                    Aspects = new List<SignalAspectDefinition>
                    {
                        new SignalAspectDefinition
                        {
                            Id = "Stop",
                            DisplayName = "Stoj!",
                            Aspect = SignalAspect.Stop,
                            MarkerAssetName = "signal_sk_2n_route_stop.png",
                            Color = "#E53935"
                        },
                        new SignalAspectDefinition
                        {
                            Id = "Route",
                            DisplayName = "Cesta dovolená",
                            Aspect = SignalAspect.ShuntingPermitted,
                            MarkerAssetName = "signal_sk_2n_route_go.png",
                            Color = "#F5F5F5"
                        }
                    }
                },

                // 3 znaky
                new SignalProfileDefinition
                {
                    Id = "3-aspect",
                    DisplayName = "Oddielové [🟡🟢🔴]",
                    HeadCount = 3,
                    Aspects = new List<SignalAspectDefinition>
                    {
                        new SignalAspectDefinition
                        {
                            Id = "Caution",
                            DisplayName = "Výstraha",
                            Aspect = SignalAspect.Caution,
                            MarkerAssetName = "signal_sk_3n_caution.png",
                            Color = "#FDD835"
                        },
                        new SignalAspectDefinition
                        {
                            Id = "Go",
                            DisplayName = "Voľno",
                            Aspect = SignalAspect.Proceed,
                            MarkerAssetName = "signal_sk_3n_go.png",
                            Color = "#43A047"
                        },
                        new SignalAspectDefinition
                        {
                            Id = "Stop",
                            DisplayName = "Stoj!",
                            Aspect = SignalAspect.Stop,
                            MarkerAssetName = "signal_sk_3n_stop.png",
                            Color = "#E53935"
                        }
                    }
                },

                // 4 znaky - Odchodové legacy/obmedzené (Z/C/B/Ž)
                // Nemá hornú žltú, preto nevie fyzicky korektne zobraziť Caution/SlowCaution/SlowExpect40.
                new SignalProfileDefinition
                {
                    Id = "4-aspect-departure",
                    DisplayName = "Odchodové legacy [🟢🔴⚪🟡]",
                    HeadCount = 4,
                    Aspects = new List<SignalAspectDefinition>
                    {
                        new SignalAspectDefinition
                        {
                            Id = "Go",
                            DisplayName = "Voľno",
                            Aspect = SignalAspect.Proceed,
                            MarkerAssetName = "signal_sk_4n_go.png",
                            Color = "#43A047"
                        },
                        new SignalAspectDefinition
                        {
                            Id = "Stop",
                            DisplayName = "Stoj!",
                            Aspect = SignalAspect.Stop,
                            MarkerAssetName = "signal_sk_4n_stop.png",
                            Color = "#E53935"
                        },
                        new SignalAspectDefinition
                        {
                            Id = "Shunt",
                            DisplayName = "Posun povolený",
                            Aspect = SignalAspect.ShuntingPermitted,
                            MarkerAssetName = "signal_sk_4n_shunt.png",
                            Color = "#F5F5F5"
                        },
                        new SignalAspectDefinition
                        {
                            Id = "Caution",
                            DisplayName = "Dolná žltá",
                            Aspect = SignalAspect.SlowProceed,
                            MarkerAssetName = "signal_sk_4n_caution.png",
                            Color = "#FDD835"
                        }
                    }
                },

                // 5 znakov - Odchodové plné SR (horná žltá / zelená / červená / biela / dolná žltá)
                new SignalProfileDefinition
                {
                    Id = "5-aspect-departure",
                    DisplayName = "Odchodové [🟡🟢🔴⚪🟡]",
                    HeadCount = 5,
                    Aspects = new List<SignalAspectDefinition>
                    {
                        new SignalAspectDefinition
                        {
                            Id = "Stop",
                            DisplayName = "Stoj!",
                            Aspect = SignalAspect.Stop,
                            MarkerAssetName = "signal_sk_5n_stop.png",
                            Color = "#E53935"
                        },
                        new SignalAspectDefinition
                        {
                            Id = "Go",
                            DisplayName = "Voľno",
                            Aspect = SignalAspect.Proceed,
                            MarkerAssetName = "signal_sk_5n_go.png",
                            Color = "#43A047"
                        },
                        new SignalAspectDefinition
                        {
                            Id = "Caution",
                            DisplayName = "Výstraha",
                            Aspect = SignalAspect.Caution,
                            MarkerAssetName = "signal_sk_5n_caution2.png",
                            Color = "#FDD835"
                        },
                        new SignalAspectDefinition
                        {
                            Id = "SlowProceed",
                            DisplayName = "40 a Voľno",
                            Aspect = SignalAspect.SlowProceed,
                            MarkerAssetName = "signal_sk_5n_caution.png",
                            Color = "#FDD835"
                        },
                        new SignalAspectDefinition
                        {
                            Id = "SlowCaution",
                            DisplayName = "40 a Výstraha",
                            Aspect = SignalAspect.SlowCaution,
                            MarkerAssetName = "signal_sk_5n_caution.png",
                            Color = "#FDD835"
                        },
                        new SignalAspectDefinition
                        {
                            Id = "SlowExpect40",
                            DisplayName = "40 a očakávaj 40",
                            Aspect = SignalAspect.SlowExpect40,
                            MarkerAssetName = "signal_sk_5n_caution2.png",
                            Color = "#FDD835"
                        },
                        new SignalAspectDefinition
                        {
                            Id = "Shunt",
                            DisplayName = "Posun povolený",
                            Aspect = SignalAspect.ShuntingPermitted,
                            MarkerAssetName = "signal_sk_5n_shunt.png",
                            Color = "#F5F5F5"
                        }
                    }
                },

                
                // 3 znaky - Vchodové (Zelená / Červená / Biela)
                new SignalProfileDefinition
                {
                    Id = "3-aspect-entry",
                    DisplayName = "Vchodové [🟢🔴⚪]",
                    HeadCount = 3,
                    Aspects = new List<SignalAspectDefinition>
                    {
                        new SignalAspectDefinition
                        {
                            Id = "Go",
                            DisplayName = "Voľno",
                            Aspect = SignalAspect.Proceed,
                            MarkerAssetName = "signal_sk_3n_entry_go.png",
                            Color = "#43A047"
                        },
                        new SignalAspectDefinition
                        {
                            Id = "Stop",
                            DisplayName = "Stoj!",
                            Aspect = SignalAspect.Stop,
                            MarkerAssetName = "signal_sk_3n_entry_stop.png",
                            Color = "#E53935"
                        },
                        new SignalAspectDefinition
                        {
                            Id = "Shunt",
                            DisplayName = "Posun povolený",
                            Aspect = SignalAspect.ShuntingPermitted,
                            MarkerAssetName = "signal_sk_3n_entry_shunt.png",
                            Color = "#F5F5F5"
                        }
                    }
                },
                
                // 4 znaky - Vchodové
                new SignalProfileDefinition
                {
                    Id = "4-aspect",
                    DisplayName = "Vchodové [🟡🔴⚪🟡]",
                    HeadCount = 4,
                    Aspects = new List<SignalAspectDefinition>
                    {
                        new SignalAspectDefinition
                        {
                            Id = "Caution",
                            DisplayName = "Výstraha",
                            Aspect = SignalAspect.Caution,
                            MarkerAssetName = "signal_sk_4n_caution.png",
                            Color = "#FDD835"
                        },
                        new SignalAspectDefinition
                        {
                            Id = "Stop",
                            DisplayName = "Stoj!",
                            Aspect = SignalAspect.Stop,
                            MarkerAssetName = "signal_sk_4n_stop.png",
                            Color = "#E53935"
                        },
                        new SignalAspectDefinition
                        {
                            Id = "Shunt",
                            DisplayName = "Posun povolený",
                            Aspect = SignalAspect.ShuntingPermitted,
                            MarkerAssetName = "signal_sk_4n_shunt.png",
                            Color = "#F5F5F5"
                        },
                        new SignalAspectDefinition
                        {
                            Id = "Caution2",
                            DisplayName = "Dolná žltá",
                            Aspect = SignalAspect.SlowProceed,
                            MarkerAssetName = "signal_sk_4n_caution2.png",
                            Color = "#FDD835"
                        }
                    }
                },

                // 5 znakov - Vchodové
                new SignalProfileDefinition
                {
                    Id = "5-aspect",
                    DisplayName = "Vchodové [🟡🟢🔴⚪🟡]",
                    HeadCount = 5,
                    Aspects = new List<SignalAspectDefinition>
                    {
                        new SignalAspectDefinition
                        {
                            Id = "Caution2",
                            DisplayName = "Horná žltá - blikanie",
                            Aspect = SignalAspect.SlowExpect40,
                            MarkerAssetName = "signal_sk_5n_caution2.png",
                            Color = "#FDD835"
                        },
                        new SignalAspectDefinition
                        {
                            Id = "Go",
                            DisplayName = "Voľno",
                            Aspect = SignalAspect.Proceed,
                            MarkerAssetName = "signal_sk_5n_go.png",
                            Color = "#43A047"
                        },
                        new SignalAspectDefinition
                        {
                            Id = "Stop",
                            DisplayName = "Stoj!",
                            Aspect = SignalAspect.Stop,
                            MarkerAssetName = "signal_sk_5n_stop.png",
                            Color = "#E53935"
                        },
                        new SignalAspectDefinition
                        {
                            Id = "Shunt",
                            DisplayName = "Posun povolený",
                            Aspect = SignalAspect.ShuntingPermitted,
                            MarkerAssetName = "signal_sk_5n_shunt.png",
                            Color = "#F5F5F5"
                        },
                        new SignalAspectDefinition
                        {
                            Id = "Caution",
                            DisplayName = "Dolná žltá",
                            Aspect = SignalAspect.SlowProceed,
                            MarkerAssetName = "signal_sk_5n_caution.png",
                            Color = "#FDD835"
                        }
                    }
                }
            }
        };
    }
}

