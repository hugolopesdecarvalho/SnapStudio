import Foundation
import Darwin

/// Em alguns Macs, a mesma tecla que dispara um atalho da SnapStudio abre também a captura interativa
/// do próprio macOS (`screencapture`), que fica com o primeiro clique/arrasto. Quando um atalho da
/// SnapStudio dispara, esta proteção termina um `screencapture` aberto nos últimos 2 segundos —
/// só esse, lançado pela mesma tecla; capturas do macOS feitas noutra altura não são afetadas.
enum SystemCaptureGuard {
    static func cancelSystemCaptureFromSameKeypress() {
        // o processo do macOS arranca uns milissegundos depois: verificar algumas vezes
        for delay in [0.05, 0.2, 0.5, 1.0] {
            DispatchQueue.main.asyncAfter(deadline: .now() + delay) { terminateRecent(maxAge: 2.0) }
        }
    }

    /// pids dos processos `screencapture` do utilizador iniciados há menos de `maxAge` segundos.
    static func recentScreencapture(maxAge: Double) -> [pid_t] {
        var mib: [Int32] = [CTL_KERN, KERN_PROC, KERN_PROC_UID, Int32(getuid())]
        var size = 0
        guard sysctl(&mib, 4, nil, &size, nil, 0) == 0, size > 0 else { return [] }
        let stride = MemoryLayout<kinfo_proc>.stride
        var procs = [kinfo_proc](repeating: kinfo_proc(), count: size / stride + 8)
        size = procs.count * stride
        guard sysctl(&mib, 4, &procs, &size, nil, 0) == 0 else { return [] }
        let now = Date().timeIntervalSince1970
        var out: [pid_t] = []
        for p in procs.prefix(size / stride) {
            var comm = p.kp_proc.p_comm
            let name = withUnsafeBytes(of: &comm) { raw -> String in
                let bytes = raw.prefix { $0 != 0 }
                return String(decoding: bytes, as: UTF8.self)
            }
            guard name == "screencapture" else { continue }
            let st = p.kp_proc.p_un.__p_starttime
            let start = Double(st.tv_sec) + Double(st.tv_usec) / 1_000_000
            if now - start < maxAge { out.append(p.kp_proc.p_pid) }
        }
        return out
    }

    static func terminateRecent(maxAge: Double) {
        for pid in recentScreencapture(maxAge: maxAge) where kill(pid, SIGTERM) == 0 {
            captureLog.notice("system screenshot tool opened by the same key was closed (pid \(pid))")
        }
    }
}
