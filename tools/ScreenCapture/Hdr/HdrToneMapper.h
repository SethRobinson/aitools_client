#pragma once
// AI Tools addition to ScreenRecorderLib 7.0.1. First-party code: repository LICENSE.md.
// This class has no capture/input APIs. It converts an existing scRGB texture to SDR.
#include <d3d11.h>
#include <d3dcompiler.h>
#include <dxgi1_6.h>
#include <wrl/client.h>
#include <vector>
#include <cstring>
#pragma comment(lib, "d3dcompiler.lib")
#pragma comment(lib, "d3d11.lib")
#pragma comment(lib, "user32.lib")

namespace AIToolsHdr {
using Microsoft::WRL::ComPtr;

// Windows reports SDR white as a multiple of scRGB's 80-nit reference, times 1000.
inline HRESULT GetSdrWhite(const wchar_t* deviceName, float* white) {
    for (int retry = 0; retry < 3; ++retry) {
        UINT32 pathCount = 0, modeCount = 0;
        LONG error = GetDisplayConfigBufferSizes(QDC_ONLY_ACTIVE_PATHS, &pathCount, &modeCount);
        if (error != ERROR_SUCCESS) return HRESULT_FROM_WIN32(error);
        std::vector<DISPLAYCONFIG_PATH_INFO> paths(pathCount);
        std::vector<DISPLAYCONFIG_MODE_INFO> modes(modeCount);
        error = QueryDisplayConfig(QDC_ONLY_ACTIVE_PATHS, &pathCount, paths.data(), &modeCount, modes.data(), nullptr);
        if (error == ERROR_INSUFFICIENT_BUFFER) continue;
        if (error != ERROR_SUCCESS) return HRESULT_FROM_WIN32(error);
        for (UINT32 i = 0; i < pathCount; ++i) {
            const auto& path = paths[i];
            DISPLAYCONFIG_SOURCE_DEVICE_NAME source{};
            source.header = { DISPLAYCONFIG_DEVICE_INFO_GET_SOURCE_NAME, sizeof(source), path.sourceInfo.adapterId, path.sourceInfo.id };
            if (DisplayConfigGetDeviceInfo(&source.header) != ERROR_SUCCESS || wcscmp(source.viewGdiDeviceName, deviceName) != 0) continue;
            DISPLAYCONFIG_SDR_WHITE_LEVEL level{};
            level.header = { DISPLAYCONFIG_DEVICE_INFO_GET_SDR_WHITE_LEVEL, sizeof(level), path.targetInfo.adapterId, path.targetInfo.id };
            error = DisplayConfigGetDeviceInfo(&level.header);
            if (error != ERROR_SUCCESS) return HRESULT_FROM_WIN32(error);
            if (level.SDRWhiteLevel == 0) return E_UNEXPECTED;
            *white = level.SDRWhiteLevel / 1000.0f;
            return S_OK;
        }
        return HRESULT_FROM_WIN32(ERROR_NOT_FOUND);
    }
    return HRESULT_FROM_WIN32(ERROR_RETRY);
}

class ToneMapper {
    ComPtr<ID3D11Device> device;
    ComPtr<ID3D11DeviceContext> deferred, immediate;
    ComPtr<ID3D11VertexShader> vertex;
    ComPtr<ID3D11PixelShader> pixel;
    ComPtr<ID3D11Buffer> constants;
    ComPtr<ID3D11Texture2D> input, output;
    ComPtr<ID3D11ShaderResourceView> sourceView;
    ComPtr<ID3D11RenderTargetView> targetView;
    UINT width = 0, height = 0;

    HRESULT Initialize(ID3D11Device* next) {
        device = next;
        device->GetImmediateContext(&immediate);
        HRESULT hr = device->CreateDeferredContext(0, &deferred);
        if (FAILED(hr)) return hr;
        // A full-screen triangle and integer Load preserve the native pixel grid.
        const char* shader = R"HLSL(
Texture2D<float4> source : register(t0);
cbuffer Settings : register(b0) { float inverseWhite; float3 padding; };
float4 VS(uint id : SV_VertexID) : SV_POSITION {
    return float4(id == 2 ? 3 : -1, id == 1 ? 3 : -1, 0, 1);
}
float3 EncodeSrgb(float3 value) {
    return float3(value.r <= 0.0031308 ? 12.92 * value.r : 1.055 * pow(value.r, 1.0 / 2.4) - 0.055,
                  value.g <= 0.0031308 ? 12.92 * value.g : 1.055 * pow(value.g, 1.0 / 2.4) - 0.055,
                  value.b <= 0.0031308 ? 12.92 * value.b : 1.055 * pow(value.b, 1.0 / 2.4) - 0.055);
}
float4 PS(float4 position : SV_POSITION) : SV_TARGET {
    float3 color = max(source.Load(int3(int2(position.xy), 0)).rgb * inverseWhite, 0);
    float peak = max(color.r, max(color.g, color.b));
    // Preserve SDR midtones, with a smooth, hue-preserving shoulder for highlights.
    // This is applied in linear light BEFORE the first 8-bit conversion.
    if (peak > 0.75) {
        float excess = peak - 0.75;
        float mapped = 0.75 + excess / (1.0 + excess / 0.25);
        color *= mapped / peak;
    }
    return float4(EncodeSrgb(color), 1);
}
)HLSL";
        ComPtr<ID3DBlob> code, errors;
        hr = D3DCompile(shader, strlen(shader), "AIToolsHdr", nullptr, nullptr, "VS", "vs_4_0", D3DCOMPILE_OPTIMIZATION_LEVEL3, 0, &code, &errors);
        if (FAILED(hr)) return hr;
        hr = device->CreateVertexShader(code->GetBufferPointer(), code->GetBufferSize(), nullptr, &vertex);
        if (FAILED(hr)) return hr;
        code.Reset(); errors.Reset();
        hr = D3DCompile(shader, strlen(shader), "AIToolsHdr", nullptr, nullptr, "PS", "ps_4_0", D3DCOMPILE_OPTIMIZATION_LEVEL3, 0, &code, &errors);
        if (FAILED(hr)) return hr;
        hr = device->CreatePixelShader(code->GetBufferPointer(), code->GetBufferSize(), nullptr, &pixel);
        if (FAILED(hr)) return hr;
        D3D11_BUFFER_DESC buffer{}; buffer.ByteWidth = 16; buffer.BindFlags = D3D11_BIND_CONSTANT_BUFFER;
        return device->CreateBuffer(&buffer, nullptr, &constants);
    }
public:
    HRESULT Convert(ID3D11Texture2D* source, float sdrWhite, ID3D11Texture2D** result) {
        if (!source || !result || !(sdrWhite > 0)) return E_INVALIDARG;
        *result = nullptr;
        D3D11_TEXTURE2D_DESC desc{}; source->GetDesc(&desc);
        if (desc.Format != DXGI_FORMAT_R16G16B16A16_FLOAT) {
            source->AddRef(); *result = source; return S_OK;
        }
        ComPtr<ID3D11Device> sourceDevice; source->GetDevice(&sourceDevice);
        HRESULT hr;
        if (sourceDevice.Get() != device.Get()) {
            *this = ToneMapper();
            hr = Initialize(sourceDevice.Get()); if (FAILED(hr)) return hr;
        }
        if (width != desc.Width || height != desc.Height) {
            input.Reset(); output.Reset(); sourceView.Reset(); targetView.Reset();
            desc.Usage = D3D11_USAGE_DEFAULT; desc.CPUAccessFlags = 0; desc.MiscFlags = 0; desc.BindFlags = D3D11_BIND_SHADER_RESOURCE;
            hr = device->CreateTexture2D(&desc, nullptr, &input); if (FAILED(hr)) return hr;
            hr = device->CreateShaderResourceView(input.Get(), nullptr, &sourceView); if (FAILED(hr)) return hr;
            desc.Format = DXGI_FORMAT_B8G8R8A8_UNORM; desc.BindFlags = D3D11_BIND_RENDER_TARGET | D3D11_BIND_SHADER_RESOURCE;
            hr = device->CreateTexture2D(&desc, nullptr, &output); if (FAILED(hr)) return hr;
            hr = device->CreateRenderTargetView(output.Get(), nullptr, &targetView); if (FAILED(hr)) return hr;
            width = desc.Width; height = desc.Height;
        }
        deferred->CopyResource(input.Get(), source);
        float values[4] = { 1.0f / sdrWhite, 0, 0, 0 };
        deferred->UpdateSubresource(constants.Get(), 0, nullptr, values, 0, 0);
        D3D11_VIEWPORT viewport = { 0, 0, static_cast<float>(width), static_cast<float>(height), 0, 1 };
        deferred->RSSetViewports(1, &viewport);
        deferred->IASetPrimitiveTopology(D3D11_PRIMITIVE_TOPOLOGY_TRIANGLELIST);
        deferred->VSSetShader(vertex.Get(), nullptr, 0); deferred->PSSetShader(pixel.Get(), nullptr, 0);
        ID3D11ShaderResourceView* srv = sourceView.Get(); deferred->PSSetShaderResources(0, 1, &srv);
        ID3D11RenderTargetView* rtv = targetView.Get(); deferred->OMSetRenderTargets(1, &rtv, nullptr);
        ID3D11Buffer* buffer = constants.Get(); deferred->PSSetConstantBuffers(0, 1, &buffer);
        deferred->Draw(3, 0);
        ComPtr<ID3D11CommandList> commands;
        hr = deferred->FinishCommandList(FALSE, &commands); if (FAILED(hr)) return hr;
        // Keep SRL's shared renderer state intact, including its cursor/dirty-rect shaders.
        immediate->ExecuteCommandList(commands.Get(), TRUE);
        return output.CopyTo(result);
    }
};
}
