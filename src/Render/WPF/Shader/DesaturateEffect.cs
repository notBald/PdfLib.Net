using System;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Effects;
using PdfLib.Res;

namespace PdfLib.Render.WPF.Shader
{
    /// <summary>
    /// From http://www.codeproject.com/KB/WPF/WPFPixelShader.aspx
    /// 
    /// Only used for testing purposes.
    /// 
    /// See also: http://rakeshravuri.blogspot.com/2008/08/blending-modes-in-wpf-using.html
    /// </summary>
    public class DesaturateEffect : ShaderEffect
    {
        public DesaturateEffect()
        {
            PixelShader = new PixelShader() { UriSource = StrRes.MakeShaderUri("Desaturate.ps") };

            UpdateShaderValue(TextureProperty);
            UpdateShaderValue(SaturationProperty);
        }

        public static readonly DependencyProperty TextureProperty =
            RegisterPixelShaderSamplerProperty("Texture", typeof(DesaturateEffect), 0);

        public Brush Texture
        {
            get { return (Brush)GetValue(TextureProperty); }
            set { SetValue(TextureProperty, value); }
        }

        public static readonly DependencyProperty SaturationProperty =
            DependencyProperty.Register("Saturation", typeof(double), typeof(DesaturateEffect),
                                        new UIPropertyMetadata(1.0, PixelShaderConstantCallback(0)));
        public double Saturation
        {
            get { return (double)GetValue(SaturationProperty); }
            set { SetValue(SaturationProperty, value); }
        }           
    }
}
