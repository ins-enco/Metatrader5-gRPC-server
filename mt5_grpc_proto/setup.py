from setuptools import setup, find_packages

setup(
    name="mt5_grpc_proto",
    version="0.3.0",
    description="Protocol Buffers and gRPC service definitions for MetaTrader 5 RPC Server",
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
    ],
    classifiers=[
        "Development Status :: 4 - Beta",
        "Intended Audience :: Developers",
        "Intended Audience :: Financial and Insurance Industry",
        "Programming Language :: Python :: 3.8",
        "Programming Language :: Python :: 3.9",
        "Programming Language :: Python :: 3.10",
        "Programming Language :: Python :: 3.11",
        "License :: OSI Approved :: MIT License",
        "Operating System :: OS Independent",
        "Topic :: Office/Business :: Financial :: Investment",
        "Topic :: Software Development :: Libraries :: Python Modules",
    ],
    python_requires=">=3.8",
    keywords="metatrader, mt5, trading, grpc, protobuf, api",
    project_urls={
        "Documentation": "https://github.com/Starmel/Metatrader5-gRPC-server/tree/main/docs",
        "Source": "https://github.com/Starmel/Metatrader5-gRPC-server",
        "Tracker": "https://github.com/Starmel/Metatrader5-gRPC-server/issues",
    },
)
