// Copyright (C) 2026 Janine Graser and Dr. Thomas Schlake
// SPDX-License-Identifier: GPL-3.0-or-later
// Additional attribution term (GPL-3.0 section 7(b)): see NOTICE in mumax-bridge/.

package main

import (
	"fmt"
	"log"
	"net"
	"sync/atomic"
)

// Single-client TCP server with a latest-frame mailbox: the solver never
// blocks on a slow client — an undelivered frame is replaced, not queued.
type server struct {
	frames chan []byte
	alive  atomic.Bool
	ready  chan struct{}
}

func newServer(port int) *server {
	s := &server{frames: make(chan []byte, 1), ready: make(chan struct{})}
	ln, err := net.Listen("tcp", fmt.Sprintf("127.0.0.1:%d", port))
	if err != nil {
		log.Fatalf("mumax-bridge: cannot listen on port %d: %v", port, err)
	}
	go s.serve(ln)
	return s
}

func (s *server) serve(ln net.Listener) {
	conn, err := ln.Accept()
	ln.Close() // one client per bridge lifetime — no orphaned GPU processes
	if err != nil {
		log.Fatalf("mumax-bridge: accept failed: %v", err)
	}
	s.alive.Store(true)
	close(s.ready)

	for frame := range s.frames {
		if _, err := conn.Write(frame); err != nil {
			s.alive.Store(false)
			conn.Close()
			return
		}
	}
}

func (s *server) waitForClient()    { <-s.ready }
func (s *server) clientAlive() bool { return s.alive.Load() }

// offer hands a frame to the writer, replacing any undelivered older frame.
func (s *server) offer(frame []byte) {
	select {
	case s.frames <- frame:
	default:
		select { // evict the stale frame if the writer hasn't taken it yet
		case <-s.frames:
		default:
		}
		select {
		case s.frames <- frame:
		default:
		}
	}
}
