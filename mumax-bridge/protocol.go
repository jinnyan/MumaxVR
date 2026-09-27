// Copyright (C) 2026 Janine Graser and Dr. Thomas Schlake
// SPDX-License-Identifier: GPL-3.0-or-later
// Additional attribution term (GPL-3.0 section 7(b)): see NOTICE in mumax-bridge/.

package main

import (
	"bytes"
	"encoding/binary"

	"github.com/mumax/3/engine"
)

const frameMagic uint32 = 0x4D583346 // "MX3F"

// encodeFrame copies M off the GPU and encodes protocol v1 (design doc §4.3):
// magic u32 · nx,ny,nz u32 · step u64 · time f64 · payload f32[n*3] x-fastest.
func encodeFrame(step uint64) []byte {
	m := engine.M.Buffer().HostCopy() // GPU → host copy of the magnetization
	v := m.Vectors()                  // [3][nz][ny][nx]float32, component index 0..2 = x,y,z
	nz := len(v[0])
	ny := len(v[0][0])
	nx := len(v[0][0][0])

	payload := make([]float32, 3*nx*ny*nz)
	idx := 0
	for k := 0; k < nz; k++ {
		for j := 0; j < ny; j++ {
			for i := 0; i < nx; i++ {
				payload[idx] = v[0][k][j][i]
				payload[idx+1] = v[1][k][j][i]
				payload[idx+2] = v[2][k][j][i]
				idx += 3
			}
		}
	}

	buf := new(bytes.Buffer)
	buf.Grow(28 + 4*len(payload))
	binary.Write(buf, binary.LittleEndian, frameMagic)
	binary.Write(buf, binary.LittleEndian, uint32(nx))
	binary.Write(buf, binary.LittleEndian, uint32(ny))
	binary.Write(buf, binary.LittleEndian, uint32(nz))
	binary.Write(buf, binary.LittleEndian, step)
	binary.Write(buf, binary.LittleEndian, engine.Time)
	binary.Write(buf, binary.LittleEndian, payload)
	return buf.Bytes()
}
