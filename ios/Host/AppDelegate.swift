import UIKit

@main
final class AppDelegate: UIResponder, UIApplicationDelegate {
    var window: UIWindow?

    func application(_ application: UIApplication,
                     didFinishLaunchingWithOptions launchOptions: [UIApplication.LaunchOptionsKey: Any]?) -> Bool {
        let window = UIWindow(frame: UIScreen.main.bounds)
        window.rootViewController = UINavigationController(rootViewController: SetupViewController())
        window.makeKeyAndVisible()
        self.window = window
        return true
    }
}

private final class SetupViewController: UIViewController {
    override func viewDidLoad() {
        super.viewDidLoad()
        title = "23×2 Kana"
        view.backgroundColor = .systemGroupedBackground
        let scroll = UIScrollView()
        let stack = UIStackView()
        stack.axis = .vertical
        stack.spacing = 20
        scroll.translatesAutoresizingMaskIntoConstraints = false
        stack.translatesAutoresizingMaskIntoConstraints = false
        view.addSubview(scroll)
        scroll.addSubview(stack)
        NSLayoutConstraint.activate([
            scroll.topAnchor.constraint(equalTo: view.safeAreaLayoutGuide.topAnchor),
            scroll.leadingAnchor.constraint(equalTo: view.leadingAnchor),
            scroll.trailingAnchor.constraint(equalTo: view.trailingAnchor),
            scroll.bottomAnchor.constraint(equalTo: view.bottomAnchor),
            stack.topAnchor.constraint(equalTo: scroll.contentLayoutGuide.topAnchor, constant: 24),
            stack.leadingAnchor.constraint(equalTo: scroll.contentLayoutGuide.leadingAnchor, constant: 20),
            stack.trailingAnchor.constraint(equalTo: scroll.contentLayoutGuide.trailingAnchor, constant: -20),
            stack.bottomAnchor.constraint(equalTo: scroll.contentLayoutGuide.bottomAnchor, constant: -24),
            stack.widthAnchor.constraint(equalTo: scroll.frameLayoutGuide.widthAnchor, constant: -40)
        ])
        addLabel("iOS 键盘测试版", font: .preferredFont(forTextStyle: .title2), to: stack)
        addLabel("1. 打开系统「设置 → 通用 → 键盘 → 键盘 → 添加新键盘」。\n2. 选择「23×2 Kana」。无需开启“允许完全访问”。\n3. 点按下方输入框，长按 🌐 切换到 23×2 Kana。", to: stack)
        let settings = UIButton(type: .system)
        settings.setTitle("打开此 App 的系统设置", for: .normal)
        settings.addTarget(self, action: #selector(openSettings), for: .touchUpInside)
        settings.heightAnchor.constraint(greaterThanOrEqualToConstant: 44).isActive = true
        stack.addArrangedSubview(settings)
        addLabel("试试输入", font: .preferredFont(forTextStyle: .headline), to: stack)
        let editor = UITextView()
        editor.font = .preferredFont(forTextStyle: .body)
        editor.adjustsFontForContentSizeCategory = true
        editor.backgroundColor = .secondarySystemGroupedBackground
        editor.layer.cornerRadius = 12
        editor.textContainerInset = UIEdgeInsets(top: 12, left: 10, bottom: 12, right: 10)
        editor.accessibilityLabel = "键盘测试输入框"
        editor.heightAnchor.constraint(equalToConstant: 150).isActive = true
        stack.addArrangedSubview(editor)
        addLabel("Z 切换两套假名板 · X 浊音\nC：第一板切换小假名，第二板切换半浊音\n⇧ 切换平假名 / 片假名\n\n输入中的假名显示在键盘顶部。点按假名行可原样确定，点候选词或空格可转换确定。输入中按回车先确定假名，再按一次换行。移动光标、切换输入框或关闭键盘会丢弃尚未确定的假名。", to: stack)
        addLabel("离线转换 · 无网络访问 · 无剪贴板历史\n\niOS 在密码和部分电话号码输入框中使用系统键盘；部分 App 也可能禁止第三方键盘。此版本是原型，转换能力尚不等同于系统日语键盘。", to: stack)
        addLabel("词典来源：SKK Development Team，GPL-2.0-or-later。项目许可和第三方声明见仓库。", font: .preferredFont(forTextStyle: .footnote), to: stack)
    }

    private func addLabel(_ text: String, font: UIFont = .preferredFont(forTextStyle: .body), to stack: UIStackView) {
        let label = UILabel()
        label.text = text
        label.font = font
        label.numberOfLines = 0
        label.adjustsFontForContentSizeCategory = true
        stack.addArrangedSubview(label)
    }

    @objc private func openSettings() {
        guard let url = URL(string: UIApplication.openSettingsURLString) else { return }
        UIApplication.shared.open(url)
    }
}
