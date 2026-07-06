(function () {
  var tracks = [
    { title: "Bloom", artist: "Troye Sivan" },
    { title: "On Top Now", artist: "NCK" },
    { title: "Blinding Lights", artist: "The Weeknd" },
    { title: "Levitating", artist: "Dua Lipa" },
    { title: "Dreams", artist: "Fleetwood Mac" },
    { title: "Heat Waves", artist: "Glass Animals" }
  ];

  var pillInfo = document.getElementById("pill-info");
  var pillTitle = document.getElementById("pill-title");
  var pillArtist = document.getElementById("pill-artist");
  var beatsPill = document.querySelector(".beats-pill");
  if (!beatsPill || !pillInfo || !pillTitle || !pillArtist) return;
  var currentIndex = 0;
  var fadeMs = window.matchMedia("(prefers-reduced-motion: reduce)").matches ? 0 : 280;

  function pickNextIndex() {
    var next = currentIndex;
    while (next === currentIndex) {
      next = Math.floor(Math.random() * tracks.length);
    }
    return next;
  }

  function updateAria() {
    if (!beatsPill) return;
    var t = tracks[currentIndex];
    beatsPill.setAttribute("aria-label", "Beats player - " + t.title + " by " + t.artist);
  }

  function cycleTrack() {
    var nextIndex = pickNextIndex();
    function apply() {
      currentIndex = nextIndex;
      pillTitle.textContent = tracks[currentIndex].title;
      pillArtist.textContent = tracks[currentIndex].artist;
      pillInfo.classList.remove("is-changing");
      updateAria();
    }
    if (fadeMs === 0) { apply(); return; }
    pillInfo.classList.add("is-changing");
    window.setTimeout(apply, fadeMs);
  }

  updateAria();
  window.setInterval(cycleTrack, 3000);
})();
