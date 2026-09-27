// Copyright (C) 2026 Janine Graser and Dr. Thomas Schlake
// SPDX-License-Identifier: GPL-3.0-or-later
// Additional attribution term (GPL-3.0 section 7(b)): see NOTICE in mumax-bridge/.

// mumax-bridge: runs a mumax3 simulation in-process and streams magnetization
// frames to MumaxVR (Unity) over localhost TCP. Protocol v1 — see README.md.
//
// Threading model: this main goroutine owns the script engine, exactly like
// the mumax3 CLI does — setup and stepping happen sequentially via Eval, so no
// InjectAndWait is needed (that mechanism exists for concurrent callers such
// as the web GUI). Phase 2 will funnel network commands into this loop.
package main

import (
	"flag"
	"fmt"
	"log"

	"github.com/mumax/3/cuda"
	"github.com/mumax/3/engine"
)

var (
	flagPort  = flag.Int("port", 35999, "localhost TCP port for the frame stream")
	flagNx    = flag.Int("nx", 256, "grid cells in x")
	flagNy    = flag.Int("ny", 256, "grid cells in y")
	flagNz    = flag.Int("nz", 1, "grid cells in z")
	flagCell  = flag.Float64("cell", 4e-9, "cell size in meters")
	flagSteps = flag.Int("steps", 25, "solver steps per streamed frame")
)

func main() {
	flag.Parse()

	cuda.Init(0)
	defer engine.Close() // flushes pending output, same as cmd/mumax3
	engine.InitIO("bridge", "bridge.out", true)

	// Same physics as scripts/reference_film.mx3: a Néel-skyrmion thin film,
	// driven by spin-polarized current so there is motion to watch.
	eval(fmt.Sprintf("SetGridSize(%d, %d, %d)", *flagNx, *flagNy, *flagNz))
	eval(fmt.Sprintf("SetCellSize(%g, %g, %g)", *flagCell, *flagCell, *flagCell))
	eval("Msat = 580e3")
	eval("Aex = 15e-12")
	eval("Dind = 3.0e-3")
	eval("Ku1 = 0.8e6")
	eval("anisU = vector(0, 0, 1)")
	eval("alpha = 0.3")
	eval("m = neelskyrmion(1, -1)")
	eval("relax()")
	eval("alpha = 0.02")
	eval("xi = 0.2")
	eval("Pol = 0.4")
	eval("J = vector(1.5e12, 0, 0)")

	srv := newServer(*flagPort)
	log.Printf("mumax-bridge: %dx%dx%d grid ready, waiting for client on 127.0.0.1:%d",
		*flagNx, *flagNy, *flagNz, *flagPort)
	srv.waitForClient()
	log.Println("mumax-bridge: client connected, streaming")

	step := uint64(0)
	for srv.clientAlive() {
		eval(fmt.Sprintf("Steps(%d)", *flagSteps))
		step += uint64(*flagSteps)
		srv.offer(encodeFrame(step))
	}
	log.Println("mumax-bridge: client disconnected — exiting")
}

// eval runs one line of mx3 script on the engine.
func eval(code string) {
	engine.Eval(code)
}
