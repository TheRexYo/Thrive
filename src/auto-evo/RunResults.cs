﻿namespace AutoEvo
{
    using System;
    using System.Collections.Generic;
    using System.Text;
    using Godot;

    /// <summary>
    ///   Container for results before they are applied.
    /// </summary>
    /// <remarks>
    ///   <para>
    ///     This is needed as earlier parts of an auto-evo run may not affect the latter parts
    ///   </para>
    /// </remarks>
    public class RunResults
    {
        private readonly Dictionary<Species, SpeciesResult> results = new Dictionary<Species, SpeciesResult>();

        public void AddMutationResultForSpecies(Species species, Species mutated)
        {
            MakeSureResultExistsForSpecies(species);

            results[species].MutatedProperties = mutated;
        }

        public void AddPopulationResultForSpecies(Species species, Patch patch, int newPopulation)
        {
            MakeSureResultExistsForSpecies(species);

            results[species].NewPopulationInPatches[patch] = Math.Max(newPopulation, 0);
        }

        public void AddMigrationResultForSpecies(Species species, Patch fromPatch, Patch toPatch, int populationAmount)
        {
            MakeSureResultExistsForSpecies(species);

            results[species].SpreadToPatches.Add(new Tuple<Patch, Patch, int>(fromPatch, toPatch, populationAmount));
        }

        public void ApplyResults(GameWorld world, bool skipMutations)
        {
            foreach (var entry in results)
            {
                if (!skipMutations && entry.Value.MutatedProperties != null)
                {
                    ApplySpeciesMutation(entry.Key, entry.Value.MutatedProperties);
                }

                foreach (var populationEntry in entry.Value.NewPopulationInPatches)
                {
                    var patch = world.Map.GetPatch(populationEntry.Key.ID);

                    if (patch != null)
                    {
                        if (!patch.UpdateSpeciesPopulation(entry.Key, populationEntry.Value))
                        {
                            GD.PrintErr("RunResults failed to update population for a species in a patch");
                        }
                    }
                    else
                    {
                        GD.PrintErr("RunResults has population of a species for invalid patch");
                    }
                }

                foreach (var spreadEntry in entry.Value.SpreadToPatches)
                {
                    var from = world.Map.GetPatch(spreadEntry.Item1.ID);
                    var to = world.Map.GetPatch(spreadEntry.Item2.ID);
                    var amount = spreadEntry.Item3;

                    if (from == null || to == null)
                    {
                        GD.PrintErr("RunResults has a species migration to/from an invalid patch");
                        continue;
                    }

                    var remainingPopulation =
                        from.GetSpeciesPopulation(entry.Key) - amount;
                    var newPopulation =
                        to.GetSpeciesPopulation(entry.Key) + amount;

                    if (!from.UpdateSpeciesPopulation(entry.Key, remainingPopulation))
                    {
                        GD.PrintErr("RunResults failed to update population for a species in a patch it moved from");
                    }

                    if (!to.UpdateSpeciesPopulation(entry.Key, newPopulation))
                    {
                        if (!to.AddSpecies(entry.Key, newPopulation))
                        {
                            GD.PrintErr("RunResults failed to update population and also add species failed on " +
                                "migration target patch");
                        }
                    }
                }
            }
        }

        /// <summary>
        ///   Sums up the populations of a species (ignores negative population)
        /// </summary>
        /// <remarks>
        ///   <para>
        ///     Throws an exception if no population is found
        ///   </para>
        /// </remarks>
        public int GetGlobalPopulation(Species species)
        {
            int result = 0;

            foreach (var entry in results[species].NewPopulationInPatches)
            {
                result += Math.Max(entry.Value, 0);
            }

            return result;
        }

        /// <summary>
        ///   variant of GetGlobalPopulation for a single patch
        /// </summary>
        public int GetPopulationInPatch(Species species, Patch patch)
        {
            return Math.Max(results[species].NewPopulationInPatches[patch], 0);
        }

        /// <summary>
        ///   Prints to log a summary of the results
        /// </summary>
        public void PrintSummary(PatchMap previousPopulations = null)
        {
            GD.Print("Start of auto-evo results summary (entries: ", results.Count, ")");

            GD.Print(MakeSummary(previousPopulations, false));

            GD.Print("End of results summary");
        }

        /// <summary>
        ///   Makes summary text
        /// </summary>
        /// <param name="previousPopulations">If provided comparisons to previous populations is included</param>
        /// <param name="playerReadable">if true ids are removed from the output</param>
        public string MakeSummary(PatchMap previousPopulations = null,
            bool playerReadable = false)
        {
            const bool resolveMoves = true;

            var builder = new StringBuilder(500);

            Func<Patch, string> patchString = (Patch patch) =>
            {
                var builder2 = new StringBuilder(80);

                if (!playerReadable)
                {
                    builder2.Append(patch.ID);
                }

                builder2.Append(" ");
                builder2.Append(patch.Name);

                return builder2.ToString();
            };

            Action<Species, Patch, int> outputPopulationForPatch = (Species species, Patch patch, int population) =>
            {
                builder.Append("  ");

                builder.Append(patchString(patch));

                builder.Append(" population: ");
                builder.Append(Math.Max(population, 0));

                if (previousPopulations != null)
                {
                    builder.Append(" previous: ");
                    builder.Append(previousPopulations.GetPatch(patch.ID).GetSpeciesPopulation(species));
                }

                builder.Append("\n");
            };

            foreach (var entry in results.Values)
            {
                builder.Append(playerReadable ? entry.Species.FormattedName : entry.Species.FormattedIdentifier);
                builder.Append(":\n");

                if (entry.MutatedProperties != null)
                {
                    builder.Append(" has a mutation");

                    if (!playerReadable)
                    {
                        builder.Append(", gene code: ");
                        builder.Append(entry.MutatedProperties.StringCode);
                    }

                    builder.Append("\n");
                }

                if (entry.SpreadToPatches.Count > 0)
                {
                    builder.Append(" spread to patches:\n");

                    foreach (var spreadEntry in entry.SpreadToPatches)
                    {
                        if (playerReadable)
                        {
                            builder.Append("  ");
                            builder.Append(spreadEntry.Item2.Name);
                            builder.Append(" by sending: ");
                            builder.Append(spreadEntry.Item3);
                            builder.Append("population");
                            builder.Append(" from patch: ");
                            builder.Append(spreadEntry.Item1.Name);
                        }
                        else
                        {
                            builder.Append("  ");
                            builder.Append(spreadEntry.Item2.Name);
                            builder.Append(" pop: ");
                            builder.Append(spreadEntry.Item3);
                            builder.Append(" from: ");
                            builder.Append(spreadEntry.Item1.Name);
                        }

                        builder.Append("\n");
                    }
                }

                builder.Append(" population in patches:\n");

                foreach (var patchPopulation in entry.NewPopulationInPatches)
                {
                    var adjustedPopulation = patchPopulation.Value;

                    if (resolveMoves)
                    {
                        adjustedPopulation +=
                            CountSpeciesSpreadPopulation(entry.Species, patchPopulation.Key);
                    }

                    outputPopulationForPatch(entry.Species, patchPopulation.Key, adjustedPopulation);
                }

                // Also print new patches the species moved to (as the moves don't get
                // included in newPopulationinpatches
                if (resolveMoves)
                {
                    foreach (var spreadEntry in entry.SpreadToPatches)
                    {
                        bool found = false;

                        var to = spreadEntry.Item2;

                        foreach (var populationEntry in entry.NewPopulationInPatches)
                        {
                            if (populationEntry.Key == to)
                            {
                                found = true;
                                break;
                            }
                        }

                        if (!found)
                        {
                            outputPopulationForPatch(entry.Species, to,
                                CountSpeciesSpreadPopulation(entry.Species, to));
                        }
                    }
                }

                if (playerReadable)
                    builder.Append("\n");
            }

            return builder.ToString();
        }

        /// <summary>
        ///   Applies the gene code and other property changes to a species
        /// </summary>
        private static void ApplySpeciesMutation(Species species, Species mutation)
        {
            throw new NotImplementedException();
        }

        private void MakeSureResultExistsForSpecies(Species species)
        {
            if (results.ContainsKey(species))
                return;

            results[species] = new SpeciesResult(species);
        }

        private int CountSpeciesSpreadPopulation(Species species,
                Patch targetPatch)
        {
            int totalPopulation = 0;

            if (!results.ContainsKey(species))
            {
                GD.PrintErr("RunResults: no species entry found for counting spread population");
                return -1;
            }

            foreach (var entry in results[species].SpreadToPatches)
            {
                if (entry.Item1 == targetPatch)
                {
                    totalPopulation -= entry.Item3;
                }
                else if (entry.Item2 == targetPatch)
                {
                    totalPopulation += entry.Item3;
                }
            }

            return totalPopulation;
        }

        public class SpeciesResult
        {
            public Species Species;

            public Dictionary<Patch, int> NewPopulationInPatches = new Dictionary<Patch, int>();

            /// <summary>
            ///   null means no changes
            /// </summary>
            public Species MutatedProperties = null;

            /// <summary>
            ///   List of patches this species has spread to
            /// </summary>
            /// <remarks>
            ///   <para>
            ///     The first part of the tuple is the patch id of the source patch, the second is the patch the
            ///     population is moved to, and the third is the amount of population to move
            ///   </para>
            /// </remarks>
            public List<Tuple<Patch, Patch, int>> SpreadToPatches = new List<Tuple<Patch, Patch, int>>();

            public SpeciesResult(Species species)
            {
                this.Species = species ?? throw new ArgumentException("species is null");
            }
        }
    }
}