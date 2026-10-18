// Synthetic textures on WARP only. No desktop capture, display query or input.
#include "../HdrToneMapper.h"
#include <DirectXPackedVector.h>
#include <array>
#include <cmath>
#include <iostream>
#include <stdexcept>
using namespace AIToolsHdr;
using DirectX::PackedVector::XMConvertFloatToHalf;
static int checks = 0;
static void Check(bool ok, const char* message) { if (!ok) throw std::runtime_error(message); ++checks; }
static void HR(HRESULT hr) { if (FAILED(hr)) { std::cerr << "HRESULT " << std::hex << hr << '\n'; throw std::runtime_error("D3D operation failed"); } }
static float Encoded(float x) { return x <= 0.0031308f ? x * 12.92f : 1.055f * std::pow(x, 1 / 2.4f) - 0.055f; }
int main() {
    try {
        ComPtr<ID3D11Device> device; ComPtr<ID3D11DeviceContext> context;
        HR(D3D11CreateDevice(nullptr, D3D_DRIVER_TYPE_WARP, nullptr, 0, nullptr, 0, D3D11_SDK_VERSION, &device, nullptr, &context));
        ToneMapper mapper;
        const float grey[] = { 0, 0.01f, 0.18f, 0.5f, 0.75f, 1, 1.5f, 2, 4, 8, 16 };
        for (float white : { 1.0f, 2.5f, 4.0f }) {
            for (UINT rows : { 2u, 6u }) {
                const UINT width = 14;
                std::vector<unsigned short> pixels(width * rows * 4);
                for (UINT y = 0; y < rows; ++y) for (UINT x = 0; x < width; ++x) for (int c = 0; c < 4; ++c)
                    pixels[(y * width + x) * 4 + c] = XMConvertFloatToHalf(c == 3 ? 1 : white * (x < 11 ? grey[x] : x - 11 == static_cast<UINT>(c) ? 0.5f : 0));
                D3D11_TEXTURE2D_DESC desc{};
                desc.Width = width; desc.Height = rows; desc.MipLevels = 1; desc.ArraySize = 1;
                desc.SampleDesc.Count = 1; desc.Format = DXGI_FORMAT_R16G16B16A16_FLOAT;
                D3D11_SUBRESOURCE_DATA data{ pixels.data(), width * 8, 0 };
                ComPtr<ID3D11Texture2D> input, output, staging;
                HR(device->CreateTexture2D(&desc, &data, &input));
                D3D11_VIEWPORT original = { 0, 0, 17, 29, 0, 1 }; context->RSSetViewports(1, &original);
                HR(mapper.Convert(input.Get(), white, &output));
                D3D11_VIEWPORT restored{}; UINT count = 1; context->RSGetViewports(&count, &restored);
                Check(restored.Width == 17 && restored.Height == 29, "Tone mapping changed recorder context state");
                output->GetDesc(&desc);
                Check(desc.Width == width && desc.Height == rows && desc.Format == DXGI_FORMAT_B8G8R8A8_UNORM, "Native dimensions/output format changed");
                desc.Usage = D3D11_USAGE_STAGING; desc.CPUAccessFlags = D3D11_CPU_ACCESS_READ; desc.BindFlags = 0;
                HR(device->CreateTexture2D(&desc, nullptr, &staging)); context->CopyResource(staging.Get(), output.Get());
                D3D11_MAPPED_SUBRESOURCE mapped{}; HR(context->Map(staging.Get(), 0, D3D11_MAP_READ, 0, &mapped));
                for (UINT y = 0; y < rows; ++y) {
                    const auto* row = static_cast<const unsigned char*>(mapped.pData) + y * mapped.RowPitch;
                    for (UINT x = 0; x < 11; ++x) {
                        float value = grey[x];
                        if (value > 0.75f) value = 0.75f + (value - 0.75f) / (1 + (value - 0.75f) / 0.25f);
                        int expected = static_cast<int>(std::round(Encoded(value) * 255));
                        Check(std::abs(static_cast<int>(row[x * 4]) - expected) <= 1 && row[x * 4] == row[x * 4 + 1] && row[x * 4] == row[x * 4 + 2] && row[x * 4 + 3] == 255, "HDR ramp, SDR white normalization or alpha mismatch");
                    }
                    Check(row[5 * 4] < row[6 * 4] && row[6 * 4] < row[8 * 4] && row[8 * 4] < row[10 * 4], "HDR highlights clipped before tone mapping");
                    for (UINT x = 11; x < 14; ++x) for (UINT c = 0; c < 3; ++c)
                        Check(std::abs(static_cast<int>(row[x * 4 + 2 - c]) - (c == x - 11 ? 188 : 0)) <= 1, "RGB/BGRA channel order mismatch");
                }
                context->Unmap(staging.Get(), 0);
                ComPtr<ID3D11Texture2D> passthrough;
                HR(mapper.Convert(output.Get(), white, &passthrough));
                Check(passthrough.Get() == output.Get(), "SDR input must pass through unchanged");
            }
        }
        Check(mapper.Convert(nullptr, 1, nullptr) == E_INVALIDARG, "Invalid input accepted");
        std::cout << "PASS: " << checks << " synthetic HDR GPU checks (WARP, no desktop/audio/input).\n";
        return 0;
    } catch (const std::exception& e) { std::cerr << e.what() << '\n'; return 1; }
}
