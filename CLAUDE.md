# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

Seth's AI Tools is a Unity-based Windows application (v2.00) that provides a native front-end for AI services like ComfyUI and Ollama. It specializes in image/video generation, LLM interactions, and interactive AI experiments.

## Essential Commands

### Build Commands
```bash
# Build Windows 64-bit version
BuildWin64.bat

# Build WebGL version  
BuildWebGL.bat

# Build and run Windows version
BuildAndPlayWin64Game.bat

# Update build date (automatically called by build scripts)
GenerateBuildDate.bat
```

### Development Requirements
- Unity 6+ required
- Open scene "Main" and click play to run in editor
- Missing fonts: Assets/GUI/GOTHIC.TFF and Assets/GUI/times.ttf may cause issues

## Architecture Overview

### Core Systems

**GameLogic.cs** - Central game manager that coordinates all major systems:
- Manages UI state and generation parameters
- Handles renderer selection (ComfyUI, DALL-E 3, etc.)
- Controls workflow and preset systems
- Coordinates between ImageGenerator and AI services

**Config.cs** - Configuration management:
- Loads/saves config.txt with server connections and API keys
- Manages multiple GPU/server configurations
- Handles LLM parameters and endpoints
- Supports multiple ComfyUI servers simultaneously

**ImageGenerator.cs** - Core image generation orchestration:
- Manages generation queue and job processing
- Handles ComfyUI workflow execution
- Coordinates with PicMain for display
- Processes masks, inpainting, and control nets

### Workflow System

The app uses a ComfyUI workflow-based architecture:
- Workflows stored in `/ComfyUI/` as JSON files
- API workflows (ending in `_api.json`) are sent to ComfyUI
- Full workflows in `/ComfyUI/FullWorkflowVersions/` for testing in ComfyUI UI
- Workflows use placeholders like `<AITOOLS_PROMPT>` replaced at runtime
- Presets in `/Presets/` chain multiple workflows together

### Key Directories

- `/Assets/_Script/` - Main application logic
- `/Assets/_Script/Pic/` - Image handling and display components
- `/Assets/_Script/GUI/` - UI components and controls
- `/Assets/Experiments/` - Interactive demos (Adventure, ShootingGallery, etc.)
- `/Assets/RT/` - Reusable toolkit components
- `/Assets/RT/AI/` - AI service integrations (OpenAI, Anthropic, ComfyUI, etc.)

### Renderer Types

Supported via `RTRendererType` enum:
- ComfyUI (primary, supports custom workflows)
- OpenAI_Dalle_3
- Legacy A1111 support (deprecated in v2.00)

### LLM Integration

Supports multiple LLMs via `LLM_Type`:
- OpenAI API (GPT-4o, o3-mini)
- Anthropic API (Claude)
- Generic LLM API (Ollama, TextGen WebUI, TabbyAPI)

Key classes:
- `GPTPromptManager` - Handles LLM prompt construction
- `OpenAITextCompletionManager` - OpenAI integration
- `AnthropicAITextCompletionManager` - Claude integration
- `TexGenWebUITextCompletionManager` - Generic LLM support

### Important Notes

- All ComfyUI servers must be started with `--listen` parameter
- Workflows must have ComfyUI Dev mode enabled to export API format
- The app strips `<think>` tags by default for reasoning models
- Uses BSD-style license requiring attribution
- Privacy-focused: no telemetry, only version checking