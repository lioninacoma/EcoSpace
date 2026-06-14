using Godot;

namespace Render
{
    /// <summary>
    /// Post-processing renderer: hosts the dithering/CRT shader on a full-screen ColorRect
    /// in the CanvasLayer. Manages the ShaderMaterial binding and exposes all tuning
    /// parameters as [Export] properties that push directly to the shader when set.
    /// </summary>
    public partial class PostProcessRenderer : ColorRect
    {
        private ShaderMaterial _material;

        // --- Backing fields ---
        private bool _dithering = true;
        private int _ditheringSize = 10;
        private int _resolutionScale = 2;
        private int _quantizeLevels = 4;

        // --- Properties mit direktem Shader-Update ---

        [Export]
        public bool Dithering
        {
            get => _dithering;
            set
            {
                _dithering = value;
                _material?.SetShaderParameter("dithering", value);
            }
        }

        [Export]
        public int DitheringSize
        {
            get => _ditheringSize;
            set
            {
                _ditheringSize = value;
                _material?.SetShaderParameter("dithering_size", value);
            }
        }

        [Export]
        public int ResolutionScale
        {
            get => _resolutionScale;
            set
            {
                _resolutionScale = Mathf.Max(1, value); // Mindestens 1
                _material?.SetShaderParameter("resolution_scale", _resolutionScale);
            }
        }

        /// <summary>
        /// Number of discrete quantize levels per RGB channel in the hue-preserving
        /// ordered dither (D-13). Default 4 → ~64 colours (4^3). Increase for more
        /// colour fidelity; decrease for a harsher retro palette look.
        /// Guarded with Mathf.Max(1, ...) so levels never reach 0 (T-02-04).
        /// </summary>
        [Export]
        public int QuantizeLevels
        {
            get => _quantizeLevels;
            set
            {
                _quantizeLevels = Mathf.Max(1, value);
                _material?.SetShaderParameter("quantize_levels", _quantizeLevels);
            }
        }

        public override void _Ready()
        {
            _material = new ShaderMaterial
            {
                Shader = GD.Load<Shader>("res://Shaders/dithering.gdshader")
            };
            Material = _material;

            // Initiale Werte in den Shader schreiben
            ApplyAllParameters();
        }

        /// <summary>Schreibt alle aktuellen Werte in den Shader.</summary>
        private void ApplyAllParameters()
        {
            _material.SetShaderParameter("dithering", _dithering);
            _material.SetShaderParameter("dithering_size", _ditheringSize);
            _material.SetShaderParameter("resolution_scale", _resolutionScale);
            _material.SetShaderParameter("quantize_levels", _quantizeLevels);
        }
    }
}