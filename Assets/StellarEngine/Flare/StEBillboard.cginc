#ifndef STE_BILLBOARD_INCLUDED
#define STE_BILLBOARD_INCLUDED

//天体に重ねるビルボード(BodyPointShader / BodySpikeShader / BodyGlareShader)の共通処理
//UnityCG.cginc の後に include する

//ビルボードの基底(ビュー空間。right・up は単位ベクトル)を作る
//・VR(ステレオ)：Quadの面を天体への向き(center)に向け、上をワールドの上方向に合わせる
//  頭を傾けても(ロール)光条やQuadが回らず、視界の端でもゆがまない
//  天頂・天底の付近ではワールドの上方向が天体への向きと重なるので、ワールドの前方向(+z)を使う
//・デスクトップ：従来どおり画面のx/y軸(VRChatのデスクトップでは視点がロールしない)
void StE_BillboardBasis(float3 center, out float3 right, out float3 up)
{
#if defined(USING_STEREO_MATRICES)
    float3 f = normalize(center);
    float3 r = cross(f, mul((float3x3)UNITY_MATRIX_V, float3(0, 1, 0)));
    if (dot(r, r) < 1e-6) { r = cross(f, mul((float3x3)UNITY_MATRIX_V, float3(0, 0, 1))); }
    right = normalize(r);
    up = cross(right, f);
#else
    right = float3(1, 0, 0);
    up = float3(0, 1, 0);
#endif
}

//ワールドの向きをビルボードの面に投影した単位ベクトル(ビュー空間)。投影できなければ0
float3 StE_ProjectToBillboard(float3 dirWorld, float3 right, float3 up)
{
    float3 d = mul((float3x3)UNITY_MATRIX_V, dirWorld);
    float3 p = right * dot(d, right) + up * dot(d, up);
    float len = length(p);
    return len > 1e-5 ? p / len : float3(0, 0, 0);
}

#endif
