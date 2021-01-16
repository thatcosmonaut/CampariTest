using System;
using SDL2;
using Campari;
using RefreshCS;
using System.Runtime.InteropServices;

namespace CampariTest
{
    [StructLayout(LayoutKind.Sequential)]
    struct Vertex
    {
        public float x, y, z;
        public float u, v;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct RaymarchUniforms
    {
        public float time, padding;
        public float resolutionX, resolutionY;
    }

    public class TestGame
    {
        bool quit = false;
        double t = 0;
        double dt = 0.01;

        ulong currentTime = SDL.SDL_GetPerformanceCounter();
        double accumulator = 0;

        RefreshDevice Device;
        IntPtr WindowHandle;

        ShaderModule passthroughVertexShaderModule;
        ShaderModule raymarchFragmentShaderModule;

        RaymarchUniforms raymarchUniforms;

        Texture woodTexture;
        Texture noiseTexture;
        Sampler sampler;

        Campari.Buffer[] vertexBuffers = new Campari.Buffer[1];
        Campari.Buffer vertexBuffer;
        UInt64[] offsets;

        Refresh.Rect renderArea;
        Refresh.Rect flip;
        Refresh.Color[] clearColors = new Refresh.Color[1];

        RenderPass mainRenderPass;

        Texture mainColorTargetTexture;
        TextureSlice mainColorTargetTextureSlice;
        ColorTarget mainColorTarget;

        Framebuffer mainFramebuffer;

        GraphicsPipeline mainGraphicsPipeline;

        Texture[] sampleTextures = new Texture[2];
        Sampler[] sampleSamplers = new Sampler[2];

        public bool Initialize(uint windowWidth, uint windowHeight)
        {
            /* Init SDL window */

            if (SDL.SDL_Init(SDL.SDL_INIT_VIDEO | SDL.SDL_INIT_TIMER | SDL.SDL_INIT_GAMECONTROLLER) < 0)
            {
                System.Console.WriteLine("Failed to initialize SDL!");
                return false;
            }

            WindowHandle = SDL.SDL_CreateWindow(
                "CampariTest",
                SDL.SDL_WINDOWPOS_UNDEFINED,
                SDL.SDL_WINDOWPOS_UNDEFINED,
                (int)windowWidth,
                (int)windowHeight,
                SDL.SDL_WindowFlags.SDL_WINDOW_VULKAN
            );

            /* Init Refresh */

            Refresh.PresentationParameters presentationParameters = new Refresh.PresentationParameters
            {
                deviceWindowHandle = WindowHandle,
                presentMode = Refresh.PresentMode.FIFO
            };

            Device = new RefreshDevice(presentationParameters, true);

            /* Init Shaders */

            passthroughVertexShaderModule = new ShaderModule(Device, new System.IO.FileInfo("passthrough_vert.spv"));
            raymarchFragmentShaderModule = new ShaderModule(Device, new System.IO.FileInfo("hexagon_grid.spv"));

            raymarchUniforms.time = 0;
            raymarchUniforms.padding = 0;
            raymarchUniforms.resolutionX = windowWidth;
            raymarchUniforms.resolutionY = windowHeight;

            /* Load Textures */

            woodTexture = Texture.LoadPNG(Device, new System.IO.FileInfo("woodgrain.png"));
            noiseTexture = Texture.LoadPNG(Device, new System.IO.FileInfo("noise.png"));

            SamplerState samplerState = SamplerState.LinearWrap;

            sampler = new Sampler(Device, ref samplerState);

            /* Load Vertex Data */

            var vertices = new Vertex[3];
            vertices[0].x = -1;
            vertices[0].y = -1;
            vertices[0].z = 0;
            vertices[0].u = 0;
            vertices[0].v = 1;

            vertices[1].x = 3;
            vertices[1].y = -1;
            vertices[1].z = 0;
            vertices[1].u = 1;
            vertices[1].v = 1;

            vertices[2].x = -1;
            vertices[2].y = 3;
            vertices[2].z = 0;
            vertices[2].u = 0;
            vertices[2].v = 0;

            vertexBuffer = new Campari.Buffer(Device, Refresh.BufferUsageFlags.Vertex, 4 * 5 * 3);
            vertexBuffer.SetData(0, vertices, 4 * 5 * 3);

            vertexBuffers[0] = vertexBuffer;

            offsets = new ulong[1];
            offsets[0] = 0;

            /* Render Pass */

            renderArea.x = 0;
            renderArea.y = 0;
            renderArea.w = (int) windowWidth;
            renderArea.h = (int) windowHeight;

            flip.x = 0;
            flip.y = (int) windowHeight;
            flip.w = (int) windowWidth;
            flip.h = -(int) windowHeight;

            clearColors[0].r = 237;
            clearColors[0].g = 41;
            clearColors[0].b = 57;
            clearColors[0].a = byte.MaxValue;

            Refresh.ColorTargetDescription colorTargetDescription = new Refresh.ColorTargetDescription
            {
                format = Refresh.ColorFormat.R8G8B8A8,
                multisampleCount = Refresh.SampleCount.One,
                loadOp = Refresh.LoadOp.Clear,
                storeOp = Refresh.StoreOp.Store
            };

            mainRenderPass = new RenderPass(Device, colorTargetDescription);

            mainColorTargetTexture = Texture.CreateTexture2D(
                Device,
                windowWidth,
                windowHeight,
                Refresh.ColorFormat.R8G8B8A8,
                Refresh.TextureUsageFlags.ColorTargetBit
            );

            mainColorTargetTextureSlice = new TextureSlice(mainColorTargetTexture);

            mainColorTarget = new ColorTarget(Device, Refresh.SampleCount.One, ref mainColorTargetTextureSlice);

            mainFramebuffer = new Framebuffer(
                Device,
                windowWidth,
                windowHeight,
                mainRenderPass,
                null,
                mainColorTarget
            );

            /* Pipeline */

            ColorTargetBlendState[] colorTargetBlendStates = new ColorTargetBlendState[1]
            {
                ColorTargetBlendState.None
            };

            ColorBlendState colorBlendState = new ColorBlendState
            {
                LogicOpEnable = false,
                LogicOp = Refresh.LogicOp.NoOp,
                BlendConstants = new BlendConstants(),
                ColorTargetBlendStates = colorTargetBlendStates
            };

            DepthStencilState depthStencilState = DepthStencilState.Disable;

            ShaderStageState vertexShaderState = new ShaderStageState
            {
                ShaderModule = passthroughVertexShaderModule,
                EntryPointName = "main",
                UniformBufferSize = 0
            };

            ShaderStageState fragmentShaderState = new ShaderStageState
            {
                ShaderModule = raymarchFragmentShaderModule,
                EntryPointName = "main",
                UniformBufferSize = 4
            };

            MultisampleState multisampleState = MultisampleState.None;

            GraphicsPipelineLayoutCreateInfo pipelineLayoutCreateInfo = new GraphicsPipelineLayoutCreateInfo
            {
                VertexSamplerBindingCount = 0,
                FragmentSamplerBindingCount = 2
            };

            RasterizerState rasterizerState = RasterizerState.CullCounterClockwise;

            var vertexBindings = new Refresh.VertexBinding[1]
            {
                new Refresh.VertexBinding
                {
                    binding = 0,
                    inputRate = Refresh.VertexInputRate.Vertex,
                    stride = 4 * 5
                }
            };

            var vertexAttributes = new Refresh.VertexAttribute[2]
            {
                new Refresh.VertexAttribute
                {
                    binding = 0,
                    location = 0,
                    format = Refresh.VertexElementFormat.Vector3,
                    offset = 0
                },
                new Refresh.VertexAttribute
                {
                    binding = 0,
                    location = 1,
                    format = Refresh.VertexElementFormat.Vector2,
                    offset = 4 * 3
                }
            };

            VertexInputState vertexInputState = new VertexInputState
            {
                VertexBindings = vertexBindings,
                VertexAttributes = vertexAttributes
            };

            var viewports = new Refresh.Viewport[1]
            {
                new Refresh.Viewport
                {
                    x = 0,
                    y = 0,
                    w = windowWidth,
                    h = windowHeight,
                    minDepth = 0,
                    maxDepth = 1
                }
            };

            var scissors = new Refresh.Rect[1]
            {
                new Refresh.Rect
                {
                    x = 0,
                    y = 0,
                    w = (int) windowWidth,
                    h = (int) windowHeight
                }
            };

            ViewportState viewportState = new ViewportState
            {
                Viewports = viewports,
                Scissors = scissors
            };

            mainGraphicsPipeline = new GraphicsPipeline(
                Device,
                colorBlendState,
                depthStencilState,
                vertexShaderState,
                fragmentShaderState,
                multisampleState,
                pipelineLayoutCreateInfo,
                rasterizerState,
                Refresh.PrimitiveType.TriangleList,
                vertexInputState,
                viewportState,
                mainRenderPass
            );

            sampleTextures[0] = woodTexture;
            sampleTextures[1] = noiseTexture;

            sampleSamplers[0] = sampler;
            sampleSamplers[1] = sampler;

            return true;
        }

        public void Run()
        {
            while (!quit)
            {
                while (SDL.SDL_PollEvent(out SDL.SDL_Event _Event) == 1)
                {
                    switch (_Event.type)
                    {
                        case SDL.SDL_EventType.SDL_QUIT:
                            quit = true;
                            break;
                    }
                }

                var newTime = SDL.SDL_GetPerformanceCounter();
                double frameTime = (newTime - currentTime) / (double)SDL.SDL_GetPerformanceFrequency();

                if (frameTime > 0.25)
                {
                    frameTime = 0.25;
                }

                currentTime = newTime;

                accumulator += frameTime;

                bool updateThisLoop = (accumulator >= dt);

                while (accumulator >= dt && !quit)
                {
                    Update(dt);

                    t += dt;
                    accumulator -= dt;
                }

                if (updateThisLoop && !quit)
                {
                    Draw();
                }
            }
        }

        public void Update(double dt)
        {
            raymarchUniforms.time += (float) dt;
        }

        public void Draw()
        {
            var commandBuffer = Device.AcquireCommandBuffer();

            commandBuffer.BeginRenderPass(
                mainRenderPass,
                mainFramebuffer,
                ref renderArea,
                clearColors
            );

            commandBuffer.BindGraphicsPipeline(mainGraphicsPipeline);
            var fragmentParamOffset = commandBuffer.PushFragmentShaderParams(raymarchUniforms);
            commandBuffer.BindVertexBuffers(0, 1, vertexBuffers, offsets);
            commandBuffer.BindFragmentSamplers(sampleTextures, sampleSamplers);
            commandBuffer.DrawPrimitives(0, 1, 0, fragmentParamOffset);
            commandBuffer.EndRenderPass();
            commandBuffer.QueuePresent(ref mainColorTargetTextureSlice, ref flip, Refresh.Filter.Nearest);

            Device.Submit(commandBuffer);
        }
    }
}
