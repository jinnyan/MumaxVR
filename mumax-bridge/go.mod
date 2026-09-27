module mumax-bridge

go 1.22.4

require github.com/mumax/3 v0.0.0

// mumax3 is not vendored (GPLv3, ~no tagged module). Clone it into the repo
// root as ./mumax3-src and this relative replace resolves. See README.md.
replace github.com/mumax/3 => ../mumax3-src
