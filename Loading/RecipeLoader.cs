﻿using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace Vintagestory.ServerMods
{
    public class RecipeLoader : ModSystem
    {
        ICoreServerAPI api;

        public override double ExecuteOrder()
        {
            return 1;
        }

        public override bool ShouldLoad(EnumAppSide side)
        {
            return side == EnumAppSide.Server;
        }


        public override void StartServerSide(ICoreServerAPI api)
        {
            this.api = api;

            api.Event.SaveGameLoaded += OnSaveGameLoaded;
        }

        public void OnSaveGameLoaded()
        {
            LoadAlloyRecipes();
            LoadCookingRecipes();
            LoadGridRecipes();
            LoadRecipes<SmithingRecipe>("smithing recipe", "recipes/smithing", (r) => api.RegisterSmithingRecipe(r));
            api.World.Logger.StoryEvent(Lang.Get("Burning sparks..."));
            LoadRecipes<ClayFormingRecipe>("clay forming recipe", "recipes/clayforming", (r) => api.RegisterClayFormingRecipe(r));
            api.World.Logger.StoryEvent(Lang.Get("Molded forms..."));
            LoadRecipes<KnappingRecipe>("knapping recipe", "recipes/knapping", (r) => api.RegisterKnappingRecipe(r));
            api.World.Logger.StoryEvent(Lang.Get("Simple tools..."));

            LoadRecipes<BarrelRecipe>("barrel recipe", "recipes/barrel", (r) => api.RegisterBarrelRecipe(r));
            //api.World.Logger.StoryEvent(Lang.Get("Simple tools..."));
        }


        public void LoadAlloyRecipes()
        {
            Dictionary<AssetLocation, AlloyRecipe> alloys = api.Assets.GetMany<AlloyRecipe>(api.Server.Logger, "recipes/alloy");

            foreach (var val in alloys)
            {
                if (!val.Value.Enabled) continue;

                val.Value.Resolve(api.World, "alloy recipe " + val.Key);
                api.RegisterMetalAlloy(val.Value);
            }

            api.World.Logger.Event("{0} metal alloys loaded", alloys.Count);
            api.World.Logger.StoryEvent(Lang.Get("Glimmers in the soil..."));
        }

        public void LoadCookingRecipes()
        {
            Dictionary<AssetLocation, CookingRecipe> recipes = api.Assets.GetMany<CookingRecipe>(api.Server.Logger, "recipes/cooking");

            foreach (var val in recipes)
            {
                if (!val.Value.Enabled) continue;

                val.Value.Resolve(api.World, "cooking recipe " + val.Key);
                api.RegisterCookingRecipe(val.Value);
            }

            api.World.Logger.Event("{0} cooking recipes loaded", recipes.Count);
            api.World.Logger.StoryEvent(Lang.Get("Taste and smell..."));
        }


        public void LoadRecipes<T>(string name, string path, System.Action<T> RegisterMethod) where T : IRecipeBase<T>
        {
            Dictionary<AssetLocation, JToken> files = api.Assets.GetMany<JToken>(api.Server.Logger, path);
            int recipeQuantity = 0;
            int quantityRegistered = 0;
            int quantityIgnored = 0;

            foreach (var val in files)
            {
                if (val.Value is JObject)
                {
                    LoadGenericRecipe(name, val.Key, val.Value.ToObject<T>(val.Key.Domain), RegisterMethod, ref quantityRegistered, ref quantityIgnored);
                    recipeQuantity++;
                }
                if (val.Value is JArray)
                {
                    foreach (var token in (val.Value as JArray))
                    {
                        LoadGenericRecipe(name, val.Key, token.ToObject<T>(val.Key.Domain), RegisterMethod, ref quantityRegistered, ref quantityIgnored);
                        recipeQuantity++;
                    }
                }
            }

            api.World.Logger.Event("{0} {1}s loaded{2}", quantityRegistered, name, quantityIgnored > 0 ? string.Format(" ({0} could not be resolved)", quantityIgnored) : "");
        }


        void LoadGenericRecipe<T>(string className, AssetLocation path, T recipe, System.Action<T> RegisterMethod, ref int quantityRegistered, ref int quantityIgnored) where T : IRecipeBase<T>
        {
            if (!recipe.Enabled) return;
            if (recipe.Name == null) recipe.Name = path;

            Dictionary<string, string[]> nameToCodeMapping = recipe.GetNameToCodeMapping(api.World);

            if (nameToCodeMapping.Count > 0)
            {
                List<T> subRecipes = new List<T>();

                int qCombs = 0;
                bool first = true;
                foreach (var val2 in nameToCodeMapping)
                {
                    if (first) qCombs = val2.Value.Length;
                    else qCombs *= val2.Value.Length;
                    first = false;
                }

                first = true;
                foreach (var val2 in nameToCodeMapping)
                {
                    string variantCode = val2.Key;
                    string[] variants = val2.Value;

                    for (int i = 0; i < qCombs; i++)
                    {
                        T rec;

                        if (first) subRecipes.Add(rec = recipe.Clone());
                        else rec = subRecipes[i];

                        if (rec.Ingredients != null)
                        {
                            foreach (var ingred in rec.Ingredients)
                            {
                                if (ingred.Name == variantCode)
                                {
                                    ingred.Code = ingred.Code.CopyWithPath(ingred.Code.Path.Replace("*", variants[i % variants.Length]));
                                }
                            }
                        }

                        rec.Output.FillPlaceHolder(val2.Key, variants[i % variants.Length]);
                    }

                    first = false;
                }

                if (subRecipes.Count == 0)
                {
                    api.World.Logger.Warning("{1} file {0} make uses of wildcards, but no blocks or item matching those wildcards were found.", path, className);
                }

                foreach (T subRecipe in subRecipes)
                {
                    if (!subRecipe.Resolve(api.World, className + " " + path))
                    {
                        quantityIgnored++;
                        continue;
                    }
                    RegisterMethod(subRecipe);
                    quantityRegistered++;
                }

            }
            else
            {
                if (!recipe.Resolve(api.World, className + " " + path))
                {
                    quantityIgnored++;
                    return;
                }

                RegisterMethod(recipe);
                quantityRegistered++;
            }
        }



        public void LoadGridRecipes()
        {
            Dictionary<AssetLocation, JToken> files = api.Assets.GetMany<JToken>(api.Server.Logger, "recipes/grid");
            int recipeQuantity=0;

            foreach (var val in files)
            {
                if (val.Value is JObject)
                {
                    LoadRecipe(val.Key, val.Value.ToObject<GridRecipe>(val.Key.Domain));
                    recipeQuantity++;
                }
                if (val.Value is JArray)
                {
                    foreach (var token in (val.Value as JArray))
                    {
                        LoadRecipe(val.Key, token.ToObject<GridRecipe>(val.Key.Domain));
                        recipeQuantity++;
                    }
                }
            }

            api.World.Logger.Event("{0} crafting recipes loaded from {1} files", recipeQuantity, files.Count);
            api.World.Logger.StoryEvent(Lang.Get("Grand inventions..."));
        }


        public void LoadRecipe(AssetLocation loc, GridRecipe recipe)
        {
            if (!recipe.Enabled) return;

            if (recipe.Name == null) recipe.Name = loc;

            Dictionary<string, string[]> nameToCodeMapping = recipe.GetNameToCodeMapping(api.World);

            if (nameToCodeMapping.Count > 0)
            {
                List<GridRecipe> subRecipes = new List<GridRecipe>();

                int qCombs = 0;
                bool first = true;
                foreach (var val2 in nameToCodeMapping)
                {
                    if (first) qCombs = val2.Value.Length;
                    else qCombs *= val2.Value.Length;
                    first = false;
                }

                first = true;
                foreach (var val2 in nameToCodeMapping)
                {
                    string variantCode = val2.Key;
                    string[] variants = val2.Value;

                    for (int i = 0; i < qCombs; i++)
                    {
                        GridRecipe rec;

                        if (first) subRecipes.Add(rec = recipe.Clone());
                        else rec = subRecipes[i];

                        foreach (CraftingRecipeIngredient ingred in rec.Ingredients.Values)
                        {
                            if (ingred.Name == variantCode)
                            {
                                ingred.Code.Path = ingred.Code.Path.Replace("*", variants[i % variants.Length]);
                            }
                        }

                        rec.Output.FillPlaceHolder(variantCode, variants[i % variants.Length]);
                    }

                    first = false;
                }

                foreach (GridRecipe subRecipe in subRecipes)
                {
                    if (!subRecipe.ResolveIngredients(api.World)) continue;
                    api.RegisterCraftingRecipe(subRecipe);
                }

            }
            else
            {
                if (!recipe.ResolveIngredients(api.World)) return;
                api.RegisterCraftingRecipe(recipe);
            }
            
        }
    }
}

