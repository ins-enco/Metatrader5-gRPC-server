from setuptools import setup, find_packages

setup(
    name="mt5_grpc_server",
    version="0.3.0",
    description="MetaTrader 5 gRPC Server for remote trading operations",
    long_description=open("README.md").read(),
    long_description_content_type="text/markdown",
    author="starmel",
    author_email="slava.kornienko16@gmail.com",
    url="https://github.com/Starmel/Metatrader5-gRPC-server",
    packages=find_packages(),
    install_requires=[
        "grpcio>=1.68.1",
        "grpcio-tools>=1.68.1",
        "protobuf>=5.29.2",
        "pytz>=2024.2",
        "MetaTrader5>=5.0.33",
        "mt5_grpc_proto>=0.3.0"
    ],
    extras_require={
        'dev': [
            'pytest>=7.0.0',
            'pytest-cov>=4.0.0',
            'black>=23.0.0',
            'isort>=5.0.0',
            'mypy>=1.0.0',
            'build>=1.0.0',
            'twine>=4.0.0',
        ],
    },
    entry_points={
        "console_scripts": [
            "mt5-grpc-server=mt5_grpc_server.grpc_server:main",
        ],
    },
    classifiers=[
        "Development Status :: 4 - Beta",
        "Intended Audience :: Developers",
        "Intended Audience :: Financial and Insurance Industry",
        "Programming Language :: Python :: 3.8",
        "Programming Language :: Python :: 3.9",
        "Programming Language :: Python :: 3.10",
        "Programming Language :: Python :: 3.11",
        "License :: OSI Approved :: MIT License",
        "Operating System :: Microsoft :: Windows",
        "Topic :: Office/Business :: Financial :: Investment",
        "Topic :: Software Development :: Libraries :: Python Modules",
    ],
    python_requires=">=3.8",
    keywords="metatrader, mt5, trading, grpc, server, api",
    project_urls={
        "Documentation": "https://github.com/Starmel/Metatrader5-gRPC-server/tree/main/docs",
        "Source": "https://github.com/Starmel/Metatrader5-gRPC-server",
        "Tracker": "https://github.com/Starmel/Metatrader5-gRPC-server/issues",
    },
)
