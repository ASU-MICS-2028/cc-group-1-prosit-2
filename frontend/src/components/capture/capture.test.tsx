import { act, fireEvent, render, screen } from "@testing-library/react"
import userEvent from "@testing-library/user-event"
import { afterEach, beforeEach, describe, expect, it, vi } from "vitest"
import "@/i18n"
import { getThemePref, setThemePref, watchSystemTheme } from "@/lib/theme"
import { DictateButton } from "./DictateButton"
import { PhotoStrip } from "./PhotoStrip"
import { SignaturePad } from "./SignaturePad"

// The shrinking step needs a real canvas; here it hands the file straight back.
vi.mock("@/lib/media", async (real) => ({
  ...(await real<typeof import("@/lib/media")>()),
  compressPhoto: vi.fn(async (file: Blob) => file),
}))

beforeEach(() => {
  URL.createObjectURL = vi.fn(() => "blob:photo")
  URL.revokeObjectURL = vi.fn()
})
afterEach(() => vi.unstubAllGlobals())

const jpeg = () => new File(["x"], "wound.jpg", { type: "image/jpeg" })

describe("PhotoStrip", () => {
  it("adds a camera photo, and removes one", async () => {
    const onChange = vi.fn()
    const { rerender } = render(<PhotoStrip photos={[]} onChange={onChange} />)
    await userEvent.upload(screen.getByLabelText("Add photo"), jpeg())
    expect(onChange).toHaveBeenCalledWith([expect.any(File)])

    const photo = jpeg()
    rerender(<PhotoStrip photos={[photo]} onChange={onChange} />)
    expect(screen.getByRole("img", { name: "Photo 1" })).toBeInTheDocument()
    await userEvent.click(
      screen.getByRole("button", { name: "Remove Photo 1" })
    )
    expect(onChange).toHaveBeenLastCalledWith([])
  })

  it("stops offering more once there are three", () => {
    render(<PhotoStrip photos={[jpeg(), jpeg(), jpeg()]} onChange={() => {}} />)
    expect(
      screen.queryByRole("button", { name: "Add photo" })
    ).not.toBeInTheDocument()
  })
})

describe("SignaturePad", () => {
  it("saves a PNG after a stroke, and Clear empties it", async () => {
    const ctx = {
      scale: vi.fn(),
      save: vi.fn(),
      restore: vi.fn(),
      setTransform: vi.fn(),
      fillRect: vi.fn(),
      beginPath: vi.fn(),
      moveTo: vi.fn(),
      lineTo: vi.fn(),
      stroke: vi.fn(),
    }
    vi.spyOn(HTMLCanvasElement.prototype, "getContext").mockReturnValue(
      ctx as unknown as CanvasRenderingContext2D
    )
    vi.spyOn(HTMLCanvasElement.prototype, "toBlob").mockImplementation((done) =>
      done(new Blob(["png"], { type: "image/png" }))
    )
    HTMLCanvasElement.prototype.setPointerCapture = vi.fn()
    const onChange = vi.fn()
    render(<SignaturePad label="Patient's signature" onChange={onChange} />)
    const pad = screen.getByRole("img", { name: "Patient's signature" })

    fireEvent.pointerDown(pad, { clientX: 10, clientY: 10, pointerId: 1 })
    fireEvent.pointerMove(pad, { clientX: 40, clientY: 20, pointerId: 1 })
    fireEvent.pointerUp(pad, { pointerId: 1 })
    expect(ctx.lineTo).toHaveBeenCalled()
    expect(onChange).toHaveBeenLastCalledWith(expect.any(Blob))
    expect(onChange.mock.lastCall![0].type).toBe("image/png")

    await userEvent.click(screen.getByRole("button", { name: "Clear" }))
    expect(onChange).toHaveBeenLastCalledWith(undefined)
    expect(screen.getByRole("button", { name: "Clear" })).toBeDisabled()
  })
})

describe("DictateButton", () => {
  it("is hidden when the phone can't recognise speech on the device", async () => {
    render(<DictateButton onText={() => {}} />)
    await act(() => Promise.resolve())
    expect(screen.queryByRole("button")).not.toBeInTheDocument()
  })
})

describe("theme", () => {
  it("remembers the choice and puts .dark on the page", () => {
    vi.stubGlobal("matchMedia", () => ({
      matches: false,
      addEventListener: vi.fn(),
    }))
    expect(getThemePref()).toBe("system")
    watchSystemTheme()
    expect(document.documentElement).not.toHaveClass("dark")
    setThemePref("dark")
    expect(getThemePref()).toBe("dark")
    expect(document.documentElement).toHaveClass("dark")
    setThemePref("light")
    expect(document.documentElement).not.toHaveClass("dark")
  })
})

describe("DictateButton with an on-device speech model", () => {
  it("types each finished phrase and stops on a second tap", async () => {
    let instance: {
      onresult: (e: unknown) => void
      onend: () => void
      processLocally?: boolean
    } | null = null
    class FakeSpeech {
      static available = vi.fn(async () => "available")
      lang = ""
      processLocally = false
      interimResults = true
      continuous = false
      onresult: ((e: unknown) => void) | null = null
      onend: (() => void) | null = null
      onerror: (() => void) | null = null
      start = () => {
        instance = this as never
      }
      stop = () => this.onend?.()
    }
    vi.stubGlobal("SpeechRecognition", FakeSpeech)
    vi.resetModules()
    const { DictateButton: Fresh } = await import("./DictateButton")
    const onText = vi.fn()
    render(<Fresh onText={onText} />)
    const button = await screen.findByRole("button", { name: "Speak notes" })
    await userEvent.click(button)
    expect(instance!.processLocally).toBe(true)
    act(() =>
      instance!.onresult({
        results: [
          Object.assign([{ transcript: " swelling on left foot " }], {
            isFinal: true,
          }),
        ],
      })
    )
    expect(onText).toHaveBeenCalledWith("swelling on left foot")
    await userEvent.click(screen.getByRole("button", { name: /Listening/ }))
    expect(
      await screen.findByRole("button", { name: "Speak notes" })
    ).toBeInTheDocument()
  })
})

describe("AudioRecorder", () => {
  it("records from the microphone, then offers playback and Record again", async () => {
    const stopTrack = vi.fn()
    vi.stubGlobal("navigator", {
      ...navigator,
      mediaDevices: {
        getUserMedia: vi.fn(async () => ({
          getTracks: () => [{ stop: stopTrack }],
        })),
      },
    })
    class FakeRecorder {
      static isTypeSupported = (m: string) => m === "audio/webm;codecs=opus"
      state = "inactive"
      mimeType = "audio/webm;codecs=opus"
      ondataavailable: ((e: { data: Blob }) => void) | null = null
      onstop: (() => void) | null = null
      start() {
        this.state = "recording"
      }
      stop() {
        this.state = "inactive"
        this.ondataavailable?.({ data: new Blob(["voice"]) })
        this.onstop?.()
      }
    }
    vi.stubGlobal("MediaRecorder", FakeRecorder)
    const { AudioRecorder } = await import("./AudioRecorder")
    const onChange = vi.fn()
    const { rerender } = render(
      <AudioRecorder
        value={undefined}
        onChange={onChange}
        onUnavailable={() => {}}
      />
    )
    await userEvent.click(
      screen.getByRole("button", { name: "Record consent" })
    )
    expect(await screen.findByText(/Recording… 30 s left/)).toBeInTheDocument()
    await userEvent.click(
      screen.getByRole("button", { name: "Stop recording" })
    )
    const clip = onChange.mock.lastCall![0] as Blob
    expect(clip.type).toBe("audio/webm;codecs=opus")
    expect(stopTrack).toHaveBeenCalled() // the microphone is released
    rerender(
      <AudioRecorder
        value={clip}
        onChange={onChange}
        onUnavailable={() => {}}
      />
    )
    expect(
      screen.getByRole("button", { name: "Record again" })
    ).toBeInTheDocument()
  })

  it("says so when the microphone is refused", async () => {
    vi.stubGlobal("navigator", {
      ...navigator,
      mediaDevices: {
        getUserMedia: vi.fn(async () => Promise.reject(new Error("denied"))),
      },
    })
    vi.stubGlobal(
      "MediaRecorder",
      class {
        static isTypeSupported = () => true
      }
    )
    const { AudioRecorder } = await import("./AudioRecorder")
    const onUnavailable = vi.fn()
    render(
      <AudioRecorder
        value={undefined}
        onChange={() => {}}
        onUnavailable={onUnavailable}
      />
    )
    await userEvent.click(
      screen.getByRole("button", { name: "Record consent" })
    )
    expect(onUnavailable).toHaveBeenCalled()
  })
})
