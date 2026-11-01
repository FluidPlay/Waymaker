HEADER
{
	Description = "Complex PBR surface shader with selectable translucency alpha source for Waymaker textures.";
	Version = 1;
}

FEATURES
{
	#include "common/features.hlsl"
	Feature( F_TRANSLUCENT, 0..1, "Translucent" );
	Feature( F_WAYMAKER_ALPHA_SOURCE, 0..1( 0 = "Color Texture Alpha", 1 = "Translucency Mask (R)" ), "Translucent" );
}

MODES
{
	Forward();
	Depth( S_MODE_DEPTH );
	ToolsShadingComplexity( "tools_shading_complexity.shader" );
}

COMMON
{
	StaticCombo( S_TRANSLUCENT, F_TRANSLUCENT, Sys( ALL ) );
	#include "common/shared.hlsl"
}

struct VertexInput
{
	#include "common/vertexinput.hlsl"
};

struct PixelInput
{
	#include "common/pixelinput.hlsl"
};

VS
{
	#include "common/vertex.hlsl"

	PixelInput MainVs( VS_INPUT input )
	{
		PixelInput output = ProcessVertex( input );
		return FinalizeVertex( output );
	}
}

PS
{
	StaticCombo( S_WAYMAKER_ALPHA_SOURCE, F_WAYMAKER_ALPHA_SOURCE, Sys( PC ) );

	#include "common/utils/Material.CommonInputs.hlsl"
	#include "common/pixel.hlsl"

	RenderState( CullMode, F_RENDER_BACKFACES ? NONE : DEFAULT );
	Texture2D g_tColorSourceAlpha < Channel( RGBA, Box( TextureColor ), Srgb ); OutputFormat( BC7 ); SrgbRead( true ); >;
	Texture2D g_tTranslucencyMask < Channel( R, Box( TextureTranslucency ), Linear ); OutputFormat( BC7 ); SrgbRead( false ); >;

	// sbox_pixel.fxc declares g_flOpacityScale for the translucent variant.

	float4 MainPs( PixelInput input ) : SV_Target0
	{
		Material material = Material::From( input );

		#if ( S_WAYMAKER_ALPHA_SOURCE == 0 )
			// Preserve all source channels; Channel(A, Box(...)) specifies a destination channel,
			// not extraction of the PNG's alpha. An RGBA sample retains the original alpha.
			material.Opacity = g_tColorSourceAlpha.Sample( TextureFiltering, input.vTextureCoords.xy ).a;
		#else
			// The separate translucency input is a grayscale mask, so use its red channel as opacity.
			material.Opacity = g_tTranslucencyMask.Sample( TextureFiltering, input.vTextureCoords.xy ).r;
		#endif

		#if ( S_TRANSLUCENT )
			material.Opacity *= g_flOpacityScale;
		#endif
		return ShadingModelStandard::Shade( input, material );
	}
}
