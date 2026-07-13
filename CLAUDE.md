# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project Overview

A gaze control demo for virtual agents in a triad conversation: two virtual agents (A and B) plus the user. Gaze behavior patterns collected from real triad conversations (gaze shifts preceding turn-taking) drive the agents' gaze control. Motion control of the agents is also data-driven.

Target demo scenes:
1. **Agent-to-agent turn-taking** — turn-taking happens between agent A and agent B while the user just watches.
2. **Turn-yielding to the user** — agent A wants the user to take the turn and signals this with gaze.

## Environment

- **Unity 6000.5.3f1** (Unity 6) with the **Universal Render Pipeline (URP)**.
- Input handled via the **Input System** package (`Assets/InputSystem_Actions.inputactions`).
- **Timeline** and **Unity Test Framework** packages are installed.
- The project is currently a fresh URP template: `Assets/Scenes/SampleScene.unity`, URP settings under `Assets/Settings/`, and removable template files under `Assets/TutorialInfo/`.

## Working in this repo

- This is a Unity project: every asset file has a paired `.meta` file. When adding, moving, or deleting assets outside the Unity Editor, keep `.meta` files consistent (let the Editor generate them where possible, and always commit them together with their asset).
- Scenes, prefabs, and most `.asset` files are Unity YAML (see `.gitattributes`); binary media (models, textures, audio) go through **Git LFS**.
- Tests use the Unity Test Framework and run inside the Unity Editor (Test Runner window, or via the JetBrains Unity test tooling when available).
