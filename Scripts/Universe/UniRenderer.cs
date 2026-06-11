using Godot;

namespace Universe
{
    public partial class UniRenderer : ColorRect
    {
        private ShaderMaterial _material;

        // --- Backing fields ---
        private bool _dithering = true;
        private int _ditheringSize = 10;
        private int _resolutionScale = 2;
        private float _threshold = 60.0f;
        private Color _white = Colors.White;
        private Color _black = Colors.Black;

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

        [Export(PropertyHint.Range, "0,255")]
        public float Threshold
        {
            get => _threshold;
            set
            {
                _threshold = Mathf.Clamp(value, 0f, 255f);
                _material?.SetShaderParameter("threshold", _threshold);
            }
        }

        [Export]
        public Color White
        {
            get => _white;
            set
            {
                _white = value;
                _material?.SetShaderParameter("white", value);
            }
        }

        [Export]
        public Color Black
        {
            get => _black;
            set
            {
                _black = value;
                _material?.SetShaderParameter("black", value);
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
            _material.SetShaderParameter("threshold", _threshold);
            _material.SetShaderParameter("white", _white);
            _material.SetShaderParameter("black", _black);
        }
    }
}