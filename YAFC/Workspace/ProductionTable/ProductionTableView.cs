using System;
using System.Collections.Generic;
using System.Linq;
using YAFC.Model;
using YAFC.UI;

namespace YAFC
{
    public class ProductionTableView : ProjectPageView<ProductionTable>
    {
        private DataColumn<RecipeRow>[] columns;
        private readonly ProductionTableFlatHierarchy flatHierarchyBuilder;

        public ProductionTableView()
        {
            columns = new[]
            {
                new DataColumn<RecipeRow>("", BuildRecipePad, null, 3f),
                new DataColumn<RecipeRow>("Recipe", BuildRecipeName, null, 13f, 16f, 30f),
                new DataColumn<RecipeRow>("Entity", BuildRecipeEntity, BuildEntityMenu, 8f), 
                new DataColumn<RecipeRow>("Ingredients", BuildRecipeIngredients, null, 32f, 16f, 40f),
                new DataColumn<RecipeRow>("Products", BuildRecipeProducts, null, 12f, 10f, 31f),
                new DataColumn<RecipeRow>("Modules", BuildRecipeModules, BuildModulesMenu, 7f), 
            };
            var grid = new DataGrid<RecipeRow>(columns);
            flatHierarchyBuilder = new ProductionTableFlatHierarchy(grid);
        }

        public override void CreateModelDropdown(ImGui gui, Type type, Project project, ref bool close)
        {
            if (gui.BuildContextMenuButton("Create production sheet"))
            {
                close = true;
                ProjectPageSettingsPanel.Show(null, (name, icon) => MainScreen.Instance.AddProjectPage(name, icon, typeof(ProductionTable), true));
            }
        }

        private void AddRecipe(ProductionTable table, Recipe recipe)
        {
            var recipeRow = new RecipeRow(table, recipe);
            table.RecordUndo().recipes.Add(recipeRow);
            recipeRow.entity = recipe.crafters.AutoSelect(DataUtils.FavouriteCrafter);
            recipeRow.fuel = recipeRow.entity.energy.fuels.AutoSelect(DataUtils.FavouriteFuel);
        }
        
        private enum ProductDropdownType
        {
            DesiredProduct,
            LinkedProduct,
            Ingredient,
            Product,
            Fuel
        }

        private void CreateLink(ProductionTable table, Goods goods)
        {
            if (table.linkMap.ContainsKey(goods))
                return;
            var link = new ProductionLink(table, goods);
            Rebuild();
            table.RecordUndo().links.Add(link);
        }

        private void DestroyLink(ProductionTable table, Goods goods)
        {
            if (table.linkMap.TryGetValue(goods, out var existing))
            {
                table.RecordUndo().links.Remove(existing);
                Rebuild();
            }
        }

        private void OpenProductDropdown(ImGui targetGui, Rect rect, Goods goods, ProductDropdownType type, RecipeRow recipe, ProductionTable context)
        {
            context.FindLink(goods, out var link);
            var comparer = DataUtils.GetRecipeComparerFor(goods);
            var allRecipes = new HashSet<Recipe>(context.recipes.Select(x => x.recipe));
            Predicate<Recipe> recipeExists = rec => allRecipes.Contains(rec); 
            Action<Recipe> addRecipe = rec =>
            {
                CreateLink(context, goods);
                if (!allRecipes.Contains(rec))
                    AddRecipe(context, rec);
            };
            var selectFuel = type != ProductDropdownType.Fuel ? null : (Action<Goods>)(fuel =>
            {
                recipe.RecordUndo().fuel = fuel;
                DataUtils.FavouriteFuel.AddToFavourite(fuel);
            });
            targetGui.ShowDropDown(rect, DropDownContent, new Padding(1f));

            void DropDownContent(ImGui gui, ref bool close)
            {
                if (type == ProductDropdownType.Fuel && (recipe.entity.energy.fuels.Count > 0))
                {
                    close |= gui.BuildInlineObejctListAndButton(recipe.entity.energy.fuels, DataUtils.FavouriteFuel, selectFuel, "Select fuel", extra:f => DataUtils.FormatAmount(f.fuelValue, UnitOfMeasure.Megajoule));
                }

                if (link != null)
                {
                    if (link.goods.fluid != null)
                        gui.BuildText("Fluid temperature: "+DataUtils.FormatAmount(link.resultTemperature, UnitOfMeasure.None) + "°");
                    if (!link.flags.HasFlags(ProductionLink.Flags.HasProduction))
                        gui.BuildText("This link has no production (Link ignored)", wrap:true, color:SchemeColor.Error);
                    if (!link.flags.HasFlags(ProductionLink.Flags.HasConsumption))
                        gui.BuildText("This link has no consumption (Link ignored)", wrap:true, color:SchemeColor.Error);
                    if (!link.flags.HasFlags(ProductionLink.Flags.HasProductionAndConsumption) && link.owner.owner is RecipeRow recipeRow && recipeRow.FindLink(link.goods, out _))
                        gui.BuildText("Nested tables have their own set of links that DON'T connect to parent links. To connect this product to the outside, remove this link", wrap:true, color:SchemeColor.Error);
                    if (link.flags.HasFlags(ProductionLink.Flags.LinkRecursiveNotMatched))
                    {
                        if (link.notMatchedFlow <= 0f)
                            gui.BuildText("YAFC was unable to satisfy this link (Negative feedback loop). This doesn't mean that this link is the problem, but it is part of the loop.", wrap:true, color:SchemeColor.Error);
                        else gui.BuildText("YAFC was unable to satisfy this link (Overproduction). You can allow overproduction for this link to solve the error.", wrap:true, color:SchemeColor.Error);
                    }
                }
                
                if (type != ProductDropdownType.Product && goods != null && goods.production.Length > 0)
                {
                    close |= gui.BuildInlineObejctListAndButton(goods.production, comparer, addRecipe, "Add production recipe", 6, true, recipeExists);
                }

                if (type != ProductDropdownType.Fuel && goods != null &&  type != ProductDropdownType.Ingredient && goods.usages.Length > 0)
                {
                    close |= gui.BuildInlineObejctListAndButton(goods.usages, DataUtils.DefaultRecipeOrdering, addRecipe, "Add consumption recipe", type == ProductDropdownType.Product ? 6 : 3, true, recipeExists);
                }
                
                if (link != null && gui.BuildCheckBox("Allow overproduction", link.algorithm == LinkAlgorithm.AllowOverProduction, out var newValue))
                    link.RecordUndo().algorithm = newValue ? LinkAlgorithm.AllowOverProduction : LinkAlgorithm.Match;

                if (link != null && link.owner == context)
                {
                    if (link.amount != 0)
                        gui.BuildText(goods.locName + " is a desired product and cannot be unlinked.", wrap:true);
                    else gui.BuildText(goods.locName+" production is currently linked. This means that YAFC will try to match production with consumption.", wrap:true);
                    if (type == ProductDropdownType.DesiredProduct)
                    {
                        if (gui.BuildButton("Remove desired product"))
                        {
                            link.RecordUndo().amount = 0;
                            close = true;
                        }

                        if (gui.BuildButton("Remove and unlink"))
                        {
                            DestroyLink(context, goods);
                            close = true;
                        }
                    } else if (link.amount == 0 && gui.BuildButton("Unlink"))
                    {
                        DestroyLink(context, goods);
                        close = true;
                    }
                }
                else if (goods != null)
                {
                    if (link != null)
                        gui.BuildText(goods.locName+" production is currently linked, but the link is outside this nested table. Nested tables can have its own separate set of links", wrap:true);
                    else gui.BuildText(goods.locName+" production is currently NOT linked. This means that YAFC will make no attempt to match production with consumption.", wrap:true);
                    if (gui.BuildButton("Create link"))
                    {
                        CreateLink(context, goods);
                        close = true;
                    }
                }
            }
        }

        private void DrawLinkedProduct(ImGui gui, ProductionLink element)
        {
            BuildGoodsIcon(gui, element.goods, element.amount, ProductDropdownType.LinkedProduct, null, model);
        }

        private void DrawDesiredProduct(ImGui gui, ProductionLink element)
        {
            gui.allocator = RectAllocator.Stretch;
            gui.spacing = 0f;
            var error = element.flags.HasFlags(ProductionLink.Flags.LinkNotMatched); 
            var evt = gui.BuildFactorioGoodsWithEditableAmount(element.goods, element.amount, element.goods.flowUnitOfMeasure, out var newAmount, error ? SchemeColor.Error : SchemeColor.Primary);
            if (evt == GoodsWithAmountEvent.ButtonClick)
                OpenProductDropdown(gui, gui.lastRect, element.goods, ProductDropdownType.DesiredProduct, null, model);
            else if (evt == GoodsWithAmountEvent.TextEditing && newAmount != 0)
                element.RecordUndo().amount = newAmount;
        }

        public override void Rebuild(bool visuaOnly = false)
        {
            base.Rebuild(visuaOnly);
            flatHierarchyBuilder.SetData(model);
            headerContent?.Rebuild();
            bodyContent?.Rebuild();
        }

        private void BuildGoodsIcon(ImGui gui, Goods goods, float amount, ProductDropdownType dropdownType, RecipeRow recipe, ProductionTable context)
        {
            var hasLink = context.FindLink(goods, out var link);
            var linkIsError = hasLink && ((link.flags & (ProductionLink.Flags.HasProductionAndConsumption | ProductionLink.Flags.LinkRecursiveNotMatched)) != ProductionLink.Flags.HasProductionAndConsumption);
            var linkIsForeign = hasLink && link.owner != context;
            if (gui.BuildFactorioObjectWithAmount(goods, amount, goods?.flowUnitOfMeasure ?? UnitOfMeasure.None, hasLink ? linkIsError ? SchemeColor.Error : linkIsForeign ? SchemeColor.Secondary : SchemeColor.Primary : SchemeColor.None) && goods != Database.voidEnergy)
            {
                OpenProductDropdown(gui, gui.lastRect, goods, dropdownType, recipe, context);
            }
        }

        private void BuildRecipeEntity(ImGui gui, RecipeRow recipe)
        {
            if (recipe.isOverviewMode)
                return;
            if (gui.BuildFactorioObjectWithAmount(recipe.entity, recipe.buildingCount, UnitOfMeasure.None) && recipe.recipe.crafters.Count > 0)
            {
                gui.ShowDropDown(((ImGui dropGui, ref bool closed) =>
                {
                    closed = dropGui.BuildInlineObejctListAndButton(recipe.recipe.crafters, DataUtils.FavouriteCrafter, sel =>
                    {
                        DataUtils.FavouriteCrafter.AddToFavourite(sel);
                        if (recipe.entity == sel)
                            return;
                        recipe.RecordUndo().entity = sel;
                        if (!sel.energy.fuels.Contains(recipe.fuel))
                            recipe.fuel = recipe.entity.energy.fuels.AutoSelect(DataUtils.FavouriteFuel);
                    }, "Select crafting entity", extra:x => DataUtils.FormatAmount(x.craftingSpeed, UnitOfMeasure.Percent));
                }));
            }

            gui.AllocateSpacing(0.5f);
            BuildGoodsIcon(gui, recipe.fuel, (float) (recipe.parameters.fuelUsagePerSecondPerRecipe * recipe.recipesPerSecond), ProductDropdownType.Fuel, recipe,
                recipe.linkRoot);
        }
        
        private void BuildTableProducts(ImGui gui, ProductionTable table, ProductionTable context, ref ImGuiUtils.InlineGridBuilder grid)
        {
            var flow = table.flow;
            var firstProduct = Array.BinarySearch(flow, new ProductionTableFlow(Database.voidEnergy, 1e-5f, 0), model);
            if (firstProduct < 0)
                firstProduct = ~firstProduct;
            for (var i = firstProduct; i < flow.Length; i++)
            { 
                grid.Next();
                BuildGoodsIcon(gui, flow[i].goods, flow[i].amount, ProductDropdownType.Product, null, context);
            }
        }
        
        private void BuildModulesMenu(ImGui gui, ref bool closed)
        {
            if (model.modules == null)
                model.RecordUndo(true).modules = new ModuleFillerParameters(model);
            gui.BuildText("Auto modules", Font.subheader);
            ModuleFillerParametersScreen.BuildSimple(gui, model.modules);
            if (gui.BuildButton("More settings"))
            {
                ModuleFillerParametersScreen.Show(model.modules);
                closed = true;
            }
        }

        private void FillRecipeList(ProductionTable table, List<RecipeRow> list)
        {
            foreach (var recipe in table.recipes)
            {
                list.Add(recipe);
                if (recipe.subgroup != null)
                    FillRecipeList(recipe.subgroup, list);
            }
        }

        private List<RecipeRow> GetRecipesRecursive()
        {
            var list = new List<RecipeRow>();
            FillRecipeList(model, list);
            return list;
        }

        private void BuildEntityMenu(ImGui gui, ref bool closed)
        {
            if (gui.BuildButton("Mass set assembler") && (closed = true))
            {
                SelectObjectPanel.Select(Database.entities.all.Where(x => x.recipes.Count > 0), "Set assembler for all recipes", set =>
                {
                    DataUtils.FavouriteCrafter.AddToFavourite(set, 10);
                    foreach (var recipe in GetRecipesRecursive())
                    {
                        if (recipe.recipe.crafters.Contains(set))
                        {
                            recipe.RecordUndo().entity = set;
                            if (!set.energy.fuels.Contains(recipe.fuel))
                                recipe.fuel = recipe.entity.energy.fuels.AutoSelect(DataUtils.FavouriteFuel);
                        }
                    }
                }, DataUtils.FavouriteCrafter, false);
            }

            if (gui.BuildButton("Mass set fuel") && (closed = true))
            {
                SelectObjectPanel.Select(Database.goods.all.Where(x => x.fuelValue > 0), "Set fuel for all recipes", set =>
                {
                    DataUtils.FavouriteFuel.AddToFavourite(set, 10);
                    foreach (var recipe in GetRecipesRecursive())
                    {
                        if (recipe.entity != null && recipe.entity.energy.fuels.Contains(set))
                            recipe.RecordUndo().fuel = set;
                    }
                }, DataUtils.FavouriteFuel, false);
            }

            if (gui.BuildButton("Shopping list") && (closed = true))
            {
                var shopList = new Dictionary<FactorioObject, int>();
                foreach (var recipe in GetRecipesRecursive())
                {
                    if (recipe.entity != null)
                    {
                        shopList.TryGetValue(recipe.entity, out var prev);
                        var count = MathUtils.Ceil(recipe.buildingCount);
                        shopList[recipe.entity] = prev + count;
                        if (recipe.parameters.modules.module != null)
                        {
                            shopList.TryGetValue(recipe.parameters.modules.module, out prev);
                            shopList[recipe.parameters.modules.module] = prev + count * recipe.parameters.modules.count;
                        }
                    }
                }
                ShoppingListScreen.Show(shopList);
            }
        }

        private void BuildRecipeModules(ImGui gui, RecipeRow recipe)
        {
            if (recipe.isOverviewMode)
                return;
            if (recipe.entity != null && recipe.entity.moduleSlots > 0 && gui.BuildFactorioObjectWithAmount(recipe.parameters.modules.module, recipe.parameters.modules.count, UnitOfMeasure.None))
            {
                gui.ShowDropDown((ImGui dropGui, ref bool closed) =>
                {
                    dropGui.BuildText("Selecting a fixed module will override auto-module filler!", wrap:true);
                    closed = dropGui.BuildInlineObejctListAndButton(recipe.recipe.modules, DataUtils.FavouriteModule, sel =>
                    {
                        DataUtils.FavouriteModule.AddToFavourite(sel);
                        if (recipe.module == sel)
                            return;
                        recipe.RecordUndo().module = sel;
                    }, "Select fixed module", allowNone:recipe.parameters.modules.module != null);
                });
            }
            if (recipe.parameters.modules.beacon != null)
                gui.BuildFactorioObjectWithAmount(recipe.parameters.modules.beacon, recipe.parameters.modules.beaconCount, UnitOfMeasure.None);
        }

        private void BuildRecipeProducts(ImGui gui, RecipeRow recipe)
        {
            var grid = gui.EnterInlineGrid(3f, 1f);
            if (recipe.isOverviewMode)
            {
                BuildTableProducts(gui, recipe.subgroup, recipe.owner, ref grid);
            }
            else
            {
                foreach (var product in recipe.recipe.products)
                {
                    grid.Next();
                    BuildGoodsIcon(gui, product.goods, (float)(product.amount * recipe.recipesPerSecond * recipe.parameters.productionMultiplier), ProductDropdownType.Product, recipe, recipe.linkRoot);
                }
            }
            grid.Dispose();
        }

        private void BuildTableIngredients(ImGui gui, ProductionTable table, ProductionTable context, ref ImGuiUtils.InlineGridBuilder grid)
        {
            foreach (var flow in table.flow)
            {
                if (flow.amount >= -1e-5f)
                    break;
                grid.Next();
                BuildGoodsIcon(gui, flow.goods, -flow.amount, ProductDropdownType.Ingredient, null, context);
            }
        }

        private void BuildRecipeIngredients(ImGui gui, RecipeRow recipe)
        {
            var grid = gui.EnterInlineGrid(3f, 1f);
            if (recipe.isOverviewMode)
            {
                BuildTableIngredients(gui, recipe.subgroup, recipe.owner, ref grid);
            }
            else
            {
                foreach (var ingredient in recipe.recipe.ingredients)
                {
                    grid.Next();
                    BuildGoodsIcon(gui, ingredient.goods, (float) (ingredient.amount * recipe.recipesPerSecond), ProductDropdownType.Ingredient, recipe, recipe.linkRoot);
                }
            }
            grid.Dispose();
        }

        private void BuildRecipeName(ImGui gui, RecipeRow recipe)
        {
            gui.spacing = 0.5f;
            if (gui.BuildFactorioObjectButton(recipe.recipe, 3f))
            {
                gui.ShowDropDown(delegate(ImGui imgui, ref bool closed)
                {
                    if (recipe.subgroup == null && imgui.BuildButton("Create nested table"))
                    {
                        recipe.RecordUndo().subgroup = new ProductionTable(recipe);
                        closed = true;
                    }

                    if (recipe.subgroup != null && imgui.BuildButton("Unpack nested table"))
                    {
                        var evacuate = recipe.subgroup.recipes;
                        recipe.subgroup.RecordUndo();
                        recipe.RecordUndo().subgroup = null;
                        var index = recipe.owner.recipes.IndexOf(recipe);
                        foreach (var evacRecipe in evacuate)
                            evacRecipe.SetOwner(recipe.owner);
                        recipe.owner.RecordUndo().recipes.InsertRange(index+1, evacuate);
                        closed = true;
                    }

                    if (recipe.subgroup != null && imgui.BuildRedButton("Remove nested table") == ImGuiUtils.Event.Click)
                    {
                        recipe.owner.RecordUndo().recipes.Remove(recipe);
                        closed = true;
                    }
                    
                    if (recipe.subgroup == null && imgui.BuildRedButton("Delete recipe") == ImGuiUtils.Event.Click)
                    {
                        recipe.owner.RecordUndo().recipes.Remove(recipe);
                        closed = true;
                    }
                });
            }
            gui.BuildText(recipe.recipe.locName, wrap:true);
        }

        protected override void BuildHeader(ImGui gui)
        {
            base.BuildHeader(gui);
            flatHierarchyBuilder.BuildHeader(gui);
        }

        private static readonly Dictionary<WarningFlags, string> WarningsMeaning = new Dictionary<WarningFlags, string>
        {
            {WarningFlags.DeadlockCandidate, "Contains recursive links that cannot be matched. No solution exists."},
            {WarningFlags.OverproductionRequired, "This model cannot be solved exactly, it requires some overproduction. You can allow overproduction for any link. This recipe contains one of the possible candidates."},
            {WarningFlags.EntityNotSpecified, "Crafter not specified. Solution is inaccurate." },
            {WarningFlags.FuelNotSpecified, "Fuel not specified. Solution is inaccurate." },
            {WarningFlags.FuelWithTemperatureNotLinked, "This recipe uses fuel with temperature. Should link with producing entity to determine temperature."},
            {WarningFlags.FuelTemperatureExceedsMaximum, "Fluid temperature is higher than generator maximum. Some energy is wasted."},
            {WarningFlags.FuelTemperatureLessThanMinimum, "Fluid temperature is lower than generator minimum. Generator will not work."},
            {WarningFlags.TemperatureForIngredientNotMatch, "This recipe does care about ingridient temperature, and the temperature range does not match"},
            {WarningFlags.TemperatureRangeForBoilerNotImplemented, "Boiler is linked production with different temperatures. Reasonong about resulting temperature is not implemented, using minimal temperature instead"},
            {WarningFlags.TemperatureRangeForFuelNotImplemented, "Fuel is linked with production with different temperatures.  Reasonong about resulting temperature is not implemented, using minimal temperature instead"}
        };
        
        private void BuildRecipePad(ImGui gui, RecipeRow row)
        {
            gui.allocator = RectAllocator.Center;
            gui.spacing = 0f;
            if (row.subgroup != null)
            {
                if (gui.BuildButton(row.subgroup.expanded ? Icon.ShevronDown : Icon.ShevronRight))
                {
                    row.subgroup.RecordUndo(true).expanded = !row.subgroup.expanded;
                    flatHierarchyBuilder.SetData(model);
                }
            }
            
            
            if (row.parameters.warningFlags != 0)
            {
                if (gui.BuildRedButton(Icon.Error) == ImGuiUtils.Event.MouseOver)
                {
                    gui.ShowTooltip(g =>
                    {
                        g.boxColor = SchemeColor.Error;
                        g.textColor = SchemeColor.ErrorText;
                        foreach (var (flag, text) in WarningsMeaning)
                        {
                            if ((row.parameters.warningFlags & flag) != 0)
                                g.BuildText(text, wrap:true);
                        }
                    });
                }
            }
            else
            {
                //gui.BuildText((index+1).ToString()); TODO
            }
        }

        protected override void BuildContent(ImGui gui)
        {
            if (model == null)
                return;
            var elementsPerRow = MathUtils.Floor((flatHierarchyBuilder.width-2f) / 4f);
            gui.spacing = 1f;
            var pad = new Padding(1f, 0.2f);
            using (gui.EnterGroup(pad))
            {
                gui.BuildText("Desired products and amounts:");
                using (var grid = gui.EnterInlineGrid(3f, 1f, elementsPerRow))
                {
                    foreach (var link in model.links)
                    {
                        if (link.amount != 0f)
                        {
                            grid.Next();
                            DrawDesiredProduct(gui, link);
                        }
                    }

                    grid.Next();
                    if (gui.BuildButton(Icon.Plus, SchemeColor.Primary, SchemeColor.PrimalyAlt, size:2.5f))
                    {
                        SelectObjectPanel.Select(Database.goods.all, "Add desired product", product =>
                        {
                            if (model.linkMap.TryGetValue(product, out var existing))
                            {
                                if (existing.amount != 0)
                                    return;
                                existing.RecordUndo().amount = 1f;
                            }
                            else
                            {
                                model.RecordUndo().links.Add(new ProductionLink(model, product) {amount = 1f});
                            }
                        });
                    }
                }
            }
            if (gui.isBuilding)
                gui.DrawRectangle(gui.lastRect, SchemeColor.Background, RectangleBorder.Thin);

            if (model.flow.Length > 0 && model.flow[0].amount < -1e-5f) 
            {
                using (gui.EnterGroup(pad))
                {
                    gui.BuildText("Summary ingredients:");
                    var grid = gui.EnterInlineGrid(3f, 1f, elementsPerRow);
                    BuildTableIngredients(gui, model, model, ref grid);
                    grid.Dispose();
                }
                if (gui.isBuilding)
                    gui.DrawRectangle(gui.lastRect, SchemeColor.Background, RectangleBorder.Thin);
            }
            
            if (model.flow.Length > 0 && model.flow[model.flow.Length - 1].amount > 1e-5f)
            {
                using (gui.EnterGroup(pad))
                {
                    gui.BuildText("Extra products:");
                    var grid = gui.EnterInlineGrid(3f, 1f, elementsPerRow);
                    BuildTableProducts(gui, model, model, ref grid);
                    grid.Dispose();
                }
                if (gui.isBuilding)
                    gui.DrawRectangle(gui.lastRect, SchemeColor.Background, RectangleBorder.Thin);
            }
            gui.AllocateSpacing();
            flatHierarchyBuilder.Build(gui);
            gui.SetMinWidth(flatHierarchyBuilder.width);
        }
    }
}