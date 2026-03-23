using System;
using System.Collections.Generic;

namespace Unity.AI.Mesh.Services.Stores.Actions.Backend.Scenario
{
    class Scenario3DModel
    {
        public string Id { get; set; }
        public ModelInfo ModelInfo { get; set; }
        public Dictionary<string, ModelParameter> Parameters { get; set; }
    }

    class ModelInfo
    {
        public string Name { get; set; }
        public string Developer { get; set; }
        public string Description { get; set; }
        public string[] GenerationModes { get; set; }
        public string[] Supports { get; set; }
        public string[] UniqueFeatures { get; set; }
        public string[] Specialties { get; set; }
    }

    class ModelParameter
    {
        public string Type { get; set; }
        public string Description { get; set; }
        public bool Required { get; set; }
        public object DefaultValue { get; set; }
        public object[] ValidValues { get; set; }
        public object[] Range { get; set; }
    }

    static class Scenario3DModels
    {
        public static readonly Scenario3DModel[] Models = {
            new()
            {
                Id = "model_rodin-hyper3d",
                ModelInfo = new ModelInfo
                {
                    Name = "(byok) Rodin Hyper3D",
                    Developer = "Scenario",
                    Description = "Multi-mode production-ready 3D model generation with flexible quality tiers",
                    GenerationModes = new[] { "Sketch", "Regular" },
                    Supports = new[] { "Image-to-3D", "Text-to-3D", "Multi-image workflows" }
                },
                Parameters = new Dictionary<string, ModelParameter>
                {
                    ["prompt"] = new()
                    {
                        Type = "string",
                        Description = "Text prompt to guide generation. Required for Text-to-3D. Optional for Image-to-3D.",
                        Required = false
                    },
                    ["input_image_urls"] = new()
                    {
                        Type = "file_array",
                        Description = "Images to use for Image-to-3D. Required for Image-to-3D, optional for Text-to-3D.",
                        Required = false
                    },
                    ["condition_mode"] = new()
                    {
                        Type = "string",
                        Description = "Determines how images are combined (fuse merges features, concat uses multi-view images).",
                        DefaultValue = "concat",
                        ValidValues = new object[] { "fuse", "concat" }
                    },
                    ["geometry_file_format"] = new()
                    {
                        Type = "string",
                        Description = "Output file format.",
                        DefaultValue = "glb",
                        ValidValues = new object[] { "glb", "usdz", "fbx", "obj", "stl" }
                    },
                    ["material"] = new()
                    {
                        Type = "string",
                        Description = "Material type for the 3D model.",
                        DefaultValue = "PBR",
                        ValidValues = new object[] { "PBR", "Shaded" }
                    },
                    ["quality"] = new()
                    {
                        Type = "string",
                        Description = "Quality of the generated 3D model.",
                        DefaultValue = "low",
                        ValidValues = new object[] { "high", "medium", "low", "extra-low" }
                    },
                    ["use_hyper"] = new()
                    {
                        Type = "boolean",
                        Description = "Whether to export the model using hyper mode.",
                        DefaultValue = true
                    },
                    ["tier"] = new()
                    {
                        Type = "string",
                        Description = "Tier of generation (Sketch or Regular).",
                        DefaultValue = "Regular",
                        ValidValues = new object[] { "Sketch", "Regular" }
                    },
                    ["TAPose"] = new()
                    {
                        Type = "boolean",
                        Description = "For human-like models, force a T/A pose.",
                        DefaultValue = false
                    },
                    ["bbox_condition"] = new()
                    {
                        Type = "number_array(3)",
                        Description = "Defines X/Y/Z dimensions of the bounding box.",
                        DefaultValue = null
                    },
                    ["seed"] = new()
                    {
                        Type = "number",
                        Description = "Randomization seed. Leave blank for random.",
                        Range = new object[] { 0, 65535 }
                    }
                }
            }
        };
    }
}
