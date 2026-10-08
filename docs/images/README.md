# README media

PNG screenshots and GIF frames come from Pegline's actual WPF controls on Windows using synthetic images. They are automated renderer demonstrations, not recordings of physical mouse gestures or the browser simulation.

Generate frames after `build.cmd`:

```bat
bin\Pegline.Tests.exe --readme-media bin\readme-media
```

Shelf frames show 0, 1, 2, 3 cards. Markup frames show the base image, an arrow, text, then the result of `EditorDocument.Undo()`. Source: `tests/VisualSmoke.cs`.

GIFs encode those PNG frames at 1.1 seconds per frame, looping. Shelf sequence: 0, 1, 2, 3, 2, 1. Markup sequence: 0, 1, 2, 3. No personal screenshots, upstream artwork, or simulated pointer clicks are included.
